import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AccessibilityInfo, type LayoutChangeEvent, StyleSheet, View } from 'react-native';
import { Gesture, GestureDetector } from 'react-native-gesture-handler';
import Animated, {
  cancelAnimation,
  runOnJS,
  useAnimatedStyle,
  useSharedValue,
  withSpring,
} from 'react-native-reanimated';
import {
  photoSwipeCapturePx,
  photoSwipeDirection,
  photoSwipeEdgeGuardPx,
  photoSwipeMaxOffAxisPx,
  photoSwipeShouldStart,
  photoSwipeTapSlopPx,
  type PhotoSwipeDirection,
} from './photoGalleryMeta';
import { runPhotoZoomOnJS } from './photoZoomGestures';
import {
  clampPhotoPanTranslation,
  clampPhotoZoomScale,
  focalPhotoZoomTranslation,
  isPhotoZoomed,
  photoZoomAccessibilityMessage,
  photoZoomDoubleTapScale,
  photoZoomMinScale,
  photoZoomSpringConfig,
} from './photoZoomMeta';
import { testIds } from '../../test/testIds';
import { ArchiveImage } from '../../ui/ArchiveImage';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';

/** Delay before the spinner so a cached hit does not flicker (AC4). */
export const photoImageLoadOverlayDelayMs = 150;

export const photoImageLoadErrorMessage = 'This photograph could not be loaded.';

type LoadStatus = 'loading' | 'loaded' | 'error';
type LoadState = { uri: string; status: LoadStatus };

type Props = {
  source: { uri: string };
  label: string;
  recyclingKey: string;
  imageWidth: number;
  imageHeight: number;
  /** Resets zoom/pan when the displayed photograph changes. */
  resetKey: number | string;
  canSwipePrevious: boolean;
  canSwipeNext: boolean;
  onGallerySwipe: (direction: PhotoSwipeDirection) => void;
  onToggleChrome: () => void;
  /** True while the screen is still showing the previous picture's metadata. */
  pending?: boolean;
};

export function ZoomableArchiveImage({
  source,
  label,
  recyclingKey,
  imageWidth,
  imageHeight,
  resetKey,
  canSwipePrevious,
  canSwipeNext,
  onGallerySwipe,
  onToggleChrome,
  pending = false,
}: Props) {
  const scale = useSharedValue(photoZoomMinScale);
  const savedScale = useSharedValue(photoZoomMinScale);
  const translateX = useSharedValue(0);
  const translateY = useSharedValue(0);
  const savedTranslateX = useSharedValue(0);
  const savedTranslateY = useSharedValue(0);
  const containerWidth = useSharedValue(0);
  const containerHeight = useSharedValue(0);
  const pinchStartScale = useSharedValue(photoZoomMinScale);
  const pinchStartTranslateX = useSharedValue(0);
  const pinchStartTranslateY = useSharedValue(0);
  const imageWidthValue = useSharedValue(imageWidth);
  const imageHeightValue = useSharedValue(imageHeight);
  const [zoomed, setZoomed] = useState(false);
  const [loadState, setLoadState] = useState<LoadState>({
    uri: source.uri,
    status: 'loading',
  });
  const [spinnerVisible, setSpinnerVisible] = useState(false);

  const canSwipePreviousRef = useRef(canSwipePrevious);
  const canSwipeNextRef = useRef(canSwipeNext);
  const onGallerySwipeRef = useRef(onGallerySwipe);
  const onToggleChromeRef = useRef(onToggleChrome);
  canSwipePreviousRef.current = canSwipePrevious;
  canSwipeNextRef.current = canSwipeNext;
  onGallerySwipeRef.current = onGallerySwipe;
  onToggleChromeRef.current = onToggleChrome;

  useEffect(() => {
    imageWidthValue.value = imageWidth;
    imageHeightValue.value = imageHeight;
  }, [imageHeight, imageHeightValue, imageWidth, imageWidthValue]);

  useEffect(() => {
    cancelAnimation(scale);
    cancelAnimation(translateX);
    cancelAnimation(translateY);
    scale.value = photoZoomMinScale;
    savedScale.value = photoZoomMinScale;
    translateX.value = 0;
    translateY.value = 0;
    savedTranslateX.value = 0;
    savedTranslateY.value = 0;
    setZoomed(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- Reanimated shared values are refs, not render deps.
  }, [resetKey]);

  const announceAndTrackZoom = useCallback((nextScale: number) => {
    setZoomed(isPhotoZoomed(nextScale));
    AccessibilityInfo.announceForAccessibility(photoZoomAccessibilityMessage(nextScale));
  }, []);

  const handleGalleryPanEnd = useCallback((dx: number, dy: number, startPageX: number) => {
    if (!photoSwipeShouldStart(startPageX)) {
      return;
    }

    const direction = photoSwipeDirection(dx, dy);
    if (direction === 'previous' && canSwipePreviousRef.current) {
      onGallerySwipeRef.current('previous');
      return;
    }
    if (direction === 'next' && canSwipeNextRef.current) {
      onGallerySwipeRef.current('next');
    }
  }, []);

  const toggleChrome = useCallback(() => {
    onToggleChromeRef.current();
  }, []);

  const onLayout = useCallback(
    (event: LayoutChangeEvent) => {
      containerWidth.value = event.nativeEvent.layout.width;
      containerHeight.value = event.nativeEvent.layout.height;
    },
    [containerHeight, containerWidth],
  );

  const imageStatus: LoadStatus =
    loadState.uri === source.uri ? loadState.status : 'loading';
  const waiting = pending || imageStatus === 'loading';
  const showError = !pending && imageStatus === 'error';

  useEffect(() => {
    if (!waiting) {
      setSpinnerVisible(false);
      return;
    }

    setSpinnerVisible(false);
    const timer = setTimeout(() => {
      setSpinnerVisible(true);
    }, photoImageLoadOverlayDelayMs);
    return () => clearTimeout(timer);
  }, [source.uri, waiting]);

  const currentUriRef = useRef(source.uri);
  currentUriRef.current = source.uri;

  const applyLoadStatus = useCallback((uri: string, status: LoadStatus) => {
    if (uri !== currentUriRef.current) {
      return;
    }
    setLoadState({ uri, status });
  }, []);

  const handleLoadStart = useCallback(() => {
    applyLoadStatus(source.uri, 'loading');
  }, [applyLoadStatus, source.uri]);

  const handleLoad = useCallback(() => {
    applyLoadStatus(source.uri, 'loaded');
  }, [applyLoadStatus, source.uri]);

  const handleError = useCallback(() => {
    applyLoadStatus(source.uri, 'error');
  }, [applyLoadStatus, source.uri]);

  const composedGesture = useMemo(() => {
    const resetZoomAnimated = (announce: boolean) => {
      'worklet';
      scale.value = withSpring(photoZoomMinScale, photoZoomSpringConfig);
      savedScale.value = photoZoomMinScale;
      translateX.value = withSpring(0, photoZoomSpringConfig);
      translateY.value = withSpring(0, photoZoomSpringConfig);
      savedTranslateX.value = 0;
      savedTranslateY.value = 0;
      if (announce) {
        runOnJS(announceAndTrackZoom)(photoZoomMinScale);
      }
    };

    const containerSize = () => {
      'worklet';
      return {
        width: containerWidth.value,
        height: containerHeight.value,
      };
    };

    const imageSize = () => {
      'worklet';
      return {
        width: imageWidthValue.value,
        height: imageHeightValue.value,
      };
    };

    const applyPan = (nextX: number, nextY: number, currentScale: number) => {
      'worklet';
      const clamped = clampPhotoPanTranslation(
        nextX,
        nextY,
        currentScale,
        containerSize(),
        imageSize(),
      );
      translateX.value = clamped.x;
      translateY.value = clamped.y;
    };

    // Pinch stays on the JS thread: same Reanimated 4 iOS worklet abort class
    // as gallery swipe. Shared-value writes from these JS handlers are allowed.
    const pinchGesture = runPhotoZoomOnJS(Gesture.Pinch())
      .onBegin(() => {
        pinchStartScale.value = scale.value;
        pinchStartTranslateX.value = translateX.value;
        pinchStartTranslateY.value = translateY.value;
      })
      .onUpdate((event) => {
        const newScale = clampPhotoZoomScale(pinchStartScale.value * event.scale);
        const focal = focalPhotoZoomTranslation(
          pinchStartTranslateX.value,
          pinchStartTranslateY.value,
          pinchStartScale.value,
          newScale,
          event.focalX,
          event.focalY,
          containerSize(),
        );
        scale.value = newScale;
        applyPan(focal.x, focal.y, newScale);
      })
      .onEnd(() => {
        if (!isPhotoZoomed(scale.value)) {
          resetZoomAnimated(true);
          return;
        }

        savedScale.value = scale.value;
        savedTranslateX.value = translateX.value;
        savedTranslateY.value = translateY.value;
        runOnJS(announceAndTrackZoom)(scale.value);
      });

    // Gallery swipe stays on the JS thread: Reanimated 4 worklets crash on iOS
    // when a Pan reads `event.allTouches` or calls `state.activate()`.
    const galleryPan = Gesture.Pan()
      .runOnJS(true)
      .maxPointers(1)
      .activeOffsetX([-photoSwipeCapturePx, photoSwipeCapturePx])
      .failOffsetY([-photoSwipeMaxOffAxisPx, photoSwipeMaxOffAxisPx])
      .onTouchesDown((event, state) => {
        const touch = event.allTouches?.[0];
        if (touch && touch.absoluteX < photoSwipeEdgeGuardPx) {
          state.fail();
        }
      })
      .onEnd((event) => {
        handleGalleryPanEnd(
          event.translationX,
          event.translationY,
          event.absoluteX - event.translationX,
        );
      });

    const zoomPan = Gesture.Pan()
      .maxPointers(1)
      .onUpdate((event) => {
        applyPan(
          savedTranslateX.value + event.translationX,
          savedTranslateY.value + event.translationY,
          scale.value,
        );
      })
      .onEnd(() => {
        savedTranslateX.value = translateX.value;
        savedTranslateY.value = translateY.value;
      });

    const doubleTapGesture = runPhotoZoomOnJS(
      Gesture.Tap().numberOfTaps(2).maxDuration(250).maxDistance(photoSwipeTapSlopPx),
    ).onEnd((event) => {
      if (isPhotoZoomed(scale.value)) {
        resetZoomAnimated(true);
        return;
      }

      const newScale = photoZoomDoubleTapScale;
      const focal = focalPhotoZoomTranslation(
        0,
        0,
        photoZoomMinScale,
        newScale,
        event.x,
        event.y,
        containerSize(),
      );
      const clamped = clampPhotoPanTranslation(
        focal.x,
        focal.y,
        newScale,
        containerSize(),
        imageSize(),
      );
      scale.value = withSpring(newScale, photoZoomSpringConfig);
      savedScale.value = newScale;
      translateX.value = withSpring(clamped.x, photoZoomSpringConfig);
      translateY.value = withSpring(clamped.y, photoZoomSpringConfig);
      savedTranslateX.value = clamped.x;
      savedTranslateY.value = clamped.y;
      runOnJS(announceAndTrackZoom)(newScale);
    });

    // Cap movement so a swipe that lifts within the tap window (a fast flick)
    // fails this gesture instead of winning the Exclusive race and eating the
    // touch before `galleryPan` gets a chance to activate.
    const singleTapGesture = Gesture.Tap()
      .runOnJS(true)
      .maxDistance(photoSwipeTapSlopPx)
      .onEnd(() => {
        toggleChrome();
      });

    return Gesture.Simultaneous(
      pinchGesture,
      Gesture.Exclusive(
        doubleTapGesture,
        singleTapGesture,
        zoomed ? zoomPan : galleryPan,
      ),
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps -- Reanimated shared values are refs, not render deps.
  }, [announceAndTrackZoom, handleGalleryPanEnd, toggleChrome, zoomed]);

  const animatedStyle = useAnimatedStyle(() => ({
    transform: [
      { translateX: translateX.value },
      { translateY: translateY.value },
      { scale: scale.value },
    ],
  }));

  const showOverlay = showError || (spinnerVisible && waiting);

  return (
    <View style={styles.frame}>
      <GestureDetector gesture={composedGesture}>
        <View
          style={styles.container}
          collapsable={false}
          onLayout={onLayout}
          accessibilityHint="Pinch or double tap to zoom. Swipe left or right to change photograph."
        >
          <Animated.View style={[styles.imageWrap, animatedStyle]}>
            <ArchiveImage
              source={source}
              label={label}
              contentFit="contain"
              recyclingKey={recyclingKey}
              priority="high"
              style={styles.image}
              onLoadStart={handleLoadStart}
              onLoad={handleLoad}
              onError={handleError}
            />
          </Animated.View>
        </View>
      </GestureDetector>
      {showOverlay ? (
        <View
          testID={testIds.photoViewerImageOverlay}
          pointerEvents="none"
          style={[StyleSheet.absoluteFill, styles.overlay]}
        >
          {showError ? (
            <ErrorBlock message={photoImageLoadErrorMessage} />
          ) : (
            <LoadingBlock label="Loading photograph…" />
          )}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  frame: {
    flex: 1,
  },
  container: {
    flex: 1,
    width: '100%',
    overflow: 'hidden',
  },
  overlay: {
    justifyContent: 'center',
    alignItems: 'center',
  },
  imageWrap: {
    flex: 1,
    width: '100%',
  },
  image: {
    flex: 1,
    width: '100%',
  },
});
