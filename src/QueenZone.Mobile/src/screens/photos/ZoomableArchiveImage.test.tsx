import { act, fireEvent, screen } from '@testing-library/react-native';
import type { ComponentProps } from 'react';
import { AccessibilityInfo } from 'react-native';
import { renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { photoSwipeTapSlopPx } from './photoGalleryMeta';
import {
  photoImageLoadErrorMessage,
  photoImageLoadOverlayDelayMs,
  ZoomableArchiveImage,
} from './ZoomableArchiveImage';

type RecordedGesture = {
  handlers: {
    onBegin?: () => void;
    onUpdate?: (event: Record<string, unknown>) => void;
    onEnd?: (event: Record<string, unknown>) => void;
    onTouchesDown?: (event: Record<string, unknown>, state: { fail: () => void }) => void;
  };
  config: Record<string, unknown>;
};

function recordedGestures(): {
  pinch: RecordedGesture;
  pan: RecordedGesture;
  zoomPan: RecordedGesture;
  tap: RecordedGesture;
  singleTap: RecordedGesture;
} {
  return jest.requireMock('react-native-gesture-handler').getRecordedGestures();
}

function renderZoom(overrides: Partial<ComponentProps<typeof ZoomableArchiveImage>> = {}) {
  const onGallerySwipe = jest.fn();
  const onToggleChrome = jest.fn();
  const result = renderWithProviders(
    <ZoomableArchiveImage
      source={{ uri: 'https://cdn.queenzone.org/brian-may/img-101.jpg' }}
      label="Live Aid"
      recyclingKey="photo-full-101"
      imageWidth={1600}
      imageHeight={900}
      resetKey={101}
      canSwipePrevious
      canSwipeNext
      onGallerySwipe={onGallerySwipe}
      onToggleChrome={onToggleChrome}
      {...overrides}
    />,
    { navigation: false },
  );
  fireEvent(screen.getByHintText(/Pinch or double tap to zoom/), 'layout', {
    nativeEvent: { layout: { x: 0, y: 0, width: 400, height: 800 } },
  });
  return { ...result, onGallerySwipe, onToggleChrome };
}

function pinchTo(scale: number, focalX = 200, focalY = 400) {
  const pinch = recordedGestures().pinch;
  act(() => {
    pinch.handlers.onBegin?.();
    pinch.handlers.onUpdate?.({ scale, focalX, focalY });
    pinch.handlers.onEnd?.({});
  });
}

function galleryPanEnd(dx: number, dy: number, startPageX = 80) {
  const pan = recordedGestures().pan;
  const fail = jest.fn();
  act(() => {
    pan.handlers.onTouchesDown?.(
      { allTouches: [{ absoluteX: startPageX, x: 40, y: 40 }] },
      { fail },
    );
    pan.handlers.onEnd?.({
      translationX: dx,
      translationY: dy,
      absoluteX: startPageX + dx,
    });
  });
  return fail;
}

describe('ZoomableArchiveImage', () => {
  beforeEach(() => {
    jest.spyOn(AccessibilityInfo, 'announceForAccessibility').mockImplementation(() => {});
  });

  it('renders the labelled archive image', () => {
    renderZoom();
    expect(screen.getByLabelText('Live Aid')).toBeOnTheScreen();
  });

  it('announces pinch zoom and a reset back to fit', () => {
    renderZoom();
    pinchTo(2);
    expect(AccessibilityInfo.announceForAccessibility).toHaveBeenCalledWith('Zoomed to 2 times');
    pinchTo(0.4);
    expect(AccessibilityInfo.announceForAccessibility).toHaveBeenCalledWith('Fit to screen');
  });

  it('pans while zoomed and ignores gallery swipe until reset', () => {
    const { onGallerySwipe, onToggleChrome } = renderZoom();
    pinchTo(2);
    act(() => {
      recordedGestures().zoomPan.handlers.onUpdate?.({ translationX: 80, translationY: 10 });
      recordedGestures().zoomPan.handlers.onEnd?.({ translationX: 80, translationY: 10 });
    });
    expect(onGallerySwipe).not.toHaveBeenCalled();
    expect(onToggleChrome).not.toHaveBeenCalled();
  });

  it('swipes previous and next at 1× and treats a tap as a chrome toggle', () => {
    const { onGallerySwipe, onToggleChrome } = renderZoom();
    galleryPanEnd(80, 0);
    expect(onGallerySwipe).toHaveBeenCalledWith('previous');
    galleryPanEnd(-80, 0);
    expect(onGallerySwipe).toHaveBeenCalledWith('next');
    act(() => recordedGestures().singleTap.handlers.onEnd?.({}));
    expect(onToggleChrome).toHaveBeenCalled();
  });

  it('runs pinch and double-tap on the JS thread like gallery swipe', () => {
    // Jest cannot catch a native Reanimated worklet abort. Lock the workaround:
    // pinch and double-tap stay on JS (same as galleryPan). zoomPan stays on
    // the UI thread unless a later PR proves it fights pinch-on-JS.
    renderZoom();
    const { pinch, tap, pan, zoomPan } = recordedGestures();
    expect(pinch.config.runOnJS).toBe(true);
    expect(tap.config.runOnJS).toBe(true);
    expect(pan.config.runOnJS).toBe(true);
    expect(zoomPan.config.runOnJS).toBeUndefined();
  });

  it('caps the tap gestures to the tap-slop distance so a fast swipe cannot win the Exclusive race', () => {
    // Regression test for the swipe-navigation break introduced when the
    // single/double-tap gestures were added ahead of the pan gesture in the
    // gallery's `Gesture.Exclusive(...)` chain: `Gesture.Tap()` has no
    // movement cap by default, so a swipe that lifts within the tap-duration
    // window (a fast flick) was classified as a tap and consumed before the
    // pan gesture ever got a chance to activate — arrow buttons kept working
    // (they don't go through gesture arbitration) while swiping silently did
    // nothing. Capping `maxDistance` to the tap slop makes the tap gestures
    // fail fast on real drag distance, letting the pan gesture win instead.
    renderZoom();
    const { tap, singleTap } = recordedGestures();
    expect(tap.config.maxDistance).toBe(photoSwipeTapSlopPx);
    expect(singleTap.config.maxDistance).toBe(photoSwipeTapSlopPx);
  });

  it('does not swipe from the iOS back edge or when neighbors are disabled', () => {
    const { onGallerySwipe, onToggleChrome } = renderZoom({
      canSwipePrevious: false,
      canSwipeNext: false,
    });
    const fail = galleryPanEnd(80, 0, 10);
    expect(fail).toHaveBeenCalled();
    galleryPanEnd(-80, 0);
    expect(onGallerySwipe).not.toHaveBeenCalled();
    expect(onToggleChrome).not.toHaveBeenCalled();
  });

  it('ignores empty touch lists', () => {
    renderZoom();
    const pan = recordedGestures().pan;
    const fail = jest.fn();
    act(() => {
      pan.handlers.onTouchesDown?.({ allTouches: [] }, { fail });
      pan.handlers.onTouchesDown?.({}, { fail });
    });
    expect(fail).not.toHaveBeenCalled();
  });

  it('double-taps to zoom in and back to fit', () => {
    renderZoom();
    const tap = recordedGestures().tap;
    act(() => tap.handlers.onEnd?.({ x: 300, y: 200 }));
    expect(AccessibilityInfo.announceForAccessibility).toHaveBeenCalledWith('Zoomed to 2 times');
    act(() => tap.handlers.onEnd?.({ x: 300, y: 200 }));
    expect(AccessibilityInfo.announceForAccessibility).toHaveBeenCalledWith('Fit to screen');
  });

  it('resets zoom when the photograph changes', () => {
    const { rerender, onGallerySwipe } = renderZoom();
    pinchTo(2);
    rerender(
      <ZoomableArchiveImage
        source={{ uri: 'https://cdn.queenzone.org/brian-may/img-102.jpg' }}
        label="Wembley"
        recyclingKey="photo-full-102"
        imageWidth={900}
        imageHeight={1600}
        resetKey={102}
        canSwipePrevious
        canSwipeNext
        onGallerySwipe={onGallerySwipe}
        onToggleChrome={jest.fn()}
      />,
    );
    fireEvent(screen.getByHintText(/Pinch or double tap to zoom/), 'layout', {
      nativeEvent: { layout: { x: 0, y: 0, width: 400, height: 800 } },
    });
    galleryPanEnd(-80, 0);
    expect(onGallerySwipe).toHaveBeenCalledWith('next');
  });

  describe('image load overlay', () => {
    afterEach(() => {
      jest.useRealTimers();
    });

    function fireImageLoad(label = 'Live Aid') {
      act(() => {
        screen.getByLabelText(label).props.onLoad?.();
      });
    }

    function fireImageError(label = 'Live Aid') {
      act(() => {
        screen.getByLabelText(label).props.onError?.();
      });
    }

    it('hides the overlay before 150ms and shows it after', () => {
      jest.useFakeTimers();
      renderZoom();
      expect(screen.queryByTestId(testIds.photoViewerImageOverlay)).toBeNull();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs - 1);
      });
      expect(screen.queryByTestId(testIds.photoViewerImageOverlay)).toBeNull();
      act(() => {
        jest.advanceTimersByTime(1);
      });
      expect(screen.getByLabelText('Loading photograph…')).toBeOnTheScreen();
      expect(screen.getByTestId(testIds.photoViewerImageOverlay).props.pointerEvents).toBe(
        'none',
      );
    });

    it('does not flicker when the image loads before the delay', () => {
      jest.useFakeTimers();
      renderZoom();
      fireImageLoad();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.queryByTestId(testIds.photoViewerImageOverlay)).toBeNull();
    });

    it('hides the overlay when the image loads', () => {
      jest.useFakeTimers();
      renderZoom();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.getByTestId(testIds.photoViewerImageOverlay)).toBeOnTheScreen();
      fireImageLoad();
      expect(screen.queryByTestId(testIds.photoViewerImageOverlay)).toBeNull();
    });

    it('shows error text and a compact retry control when the image fails', () => {
      jest.useFakeTimers();
      renderZoom();
      fireImageError();
      expect(screen.getByText('Unable to load')).toBeOnTheScreen();
      expect(screen.getByText(photoImageLoadErrorMessage)).toBeOnTheScreen();
      expect(screen.getByRole('button', { name: 'Tap to retry' })).toBeOnTheScreen();
      expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
      expect(screen.queryByLabelText('Loading photograph…')).toBeNull();
      expect(screen.getByTestId(testIds.photoViewerImageOverlay).props.pointerEvents).toBe(
        'box-none',
      );
    });

    it('remounts the image on retry and shows the spinner after the delay', () => {
      jest.useFakeTimers();
      renderZoom();
      fireImageError();
      fireEvent.press(screen.getByTestId(testIds.photoViewerImageRetry));
      expect(screen.queryByTestId(testIds.photoViewerImageRetry)).toBeNull();
      expect(screen.queryByTestId(testIds.photoViewerImageOverlay)).toBeNull();
      expect(screen.getByLabelText('Live Aid')).toBeOnTheScreen();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.getByLabelText('Loading photograph…')).toBeOnTheScreen();
      expect(screen.getByTestId(testIds.photoViewerImageOverlay).props.pointerEvents).toBe(
        'none',
      );
    });

    it('does not show retry until the image fails', () => {
      jest.useFakeTimers();
      renderZoom();
      expect(screen.queryByTestId(testIds.photoViewerImageRetry)).toBeNull();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.queryByTestId(testIds.photoViewerImageRetry)).toBeNull();
    });

    it('ignores a stale onLoad after the source changes', () => {
      jest.useFakeTimers();
      const { rerender, onGallerySwipe } = renderZoom();
      const staleOnLoad = screen.getByLabelText('Live Aid').props.onLoad as () => void;
      rerender(
        <ZoomableArchiveImage
          source={{ uri: 'https://cdn.queenzone.org/brian-may/img-102.jpg' }}
          label="Wembley"
          recyclingKey="photo-full-102"
          imageWidth={900}
          imageHeight={1600}
          resetKey={102}
          canSwipePrevious
          canSwipeNext
          onGallerySwipe={onGallerySwipe}
          onToggleChrome={jest.fn()}
        />,
      );
      act(() => {
        staleOnLoad();
      });
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.getByLabelText('Wembley')).toBeOnTheScreen();
      expect(screen.getByLabelText('Loading photograph…')).toBeOnTheScreen();
    });

    it('fires swipe callbacks while the overlay is showing', () => {
      jest.useFakeTimers();
      const { onGallerySwipe } = renderZoom();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.getByTestId(testIds.photoViewerImageOverlay).props.pointerEvents).toBe(
        'none',
      );
      galleryPanEnd(80, 0);
      expect(onGallerySwipe).toHaveBeenCalledWith('previous');
      galleryPanEnd(-80, 0);
      expect(onGallerySwipe).toHaveBeenCalledWith('next');
    });

    it('fires swipe callbacks while the error overlay is showing', () => {
      jest.useFakeTimers();
      const { onGallerySwipe } = renderZoom();
      fireImageError();
      expect(screen.getByTestId(testIds.photoViewerImageOverlay).props.pointerEvents).toBe(
        'box-none',
      );
      galleryPanEnd(-80, 0);
      expect(onGallerySwipe).toHaveBeenCalledWith('next');
    });

    it('keeps the recorded gesture config unchanged while the overlay is showing', () => {
      jest.useFakeTimers();
      renderZoom();
      act(() => {
        jest.advanceTimersByTime(photoImageLoadOverlayDelayMs);
      });
      expect(screen.getByTestId(testIds.photoViewerImageOverlay)).toBeOnTheScreen();
      const { pinch, tap, pan, zoomPan, singleTap } = recordedGestures();
      expect(pinch.config.runOnJS).toBe(true);
      expect(tap.config.runOnJS).toBe(true);
      expect(pan.config.runOnJS).toBe(true);
      expect(zoomPan.config.runOnJS).toBeUndefined();
      expect(tap.config.maxDistance).toBe(photoSwipeTapSlopPx);
      expect(singleTap.config.maxDistance).toBe(photoSwipeTapSlopPx);
    });
  });
});
