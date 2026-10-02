import { PhotoViewerChrome, type ViewerStatus, type ViewerStatusKind } from './PhotoViewerChrome';
import { photoViewerDisplay } from './photoViewerDisplay';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, Platform, Text, View } from 'react-native';
import { ApiError, fetchPhotoDetail, type PhotoDetail } from '../../api';
import type { PhotosStackParamList } from '../../navigation/types';
import { testIds } from '../../test/testIds';
import { type, useTheme } from '../../theme';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import {
  photoViewerParams,
  resolvedPhotoSize,
  schedulePhotoGallerySwipe,
} from './photoGalleryMeta';
import { saveGalleryPhoto, saveGalleryPhotoCopy } from './saveGalleryPhoto';
import { setAndroidGalleryWallpaper } from './setGalleryWallpaper';
import { WallpaperTargetSheet } from './WallpaperTargetSheet';
import { wallpaperCopy, type WallpaperTarget } from './wallpaperMeta';
import { usePrefetchNeighbours } from './usePrefetchNeighbours';
import { ZoomableArchiveImage } from './ZoomableArchiveImage';

type Props = NativeStackScreenProps<PhotosStackParamList, 'PhotoViewer'>;


export const photoViewerStatusTiming = {
  successDismissMs: 2800,
  errorDismissMs: 5000,
  cooldownMs: 4000,
} as const;

function clearTimeoutRef(ref: { current: ReturnType<typeof setTimeout> | null }) {
  if (ref.current != null) {
    clearTimeout(ref.current);
    ref.current = null;
  }
}

export function PhotoViewerScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { slug, picId, size } = route.params;
  const [chromeVisible, setChromeVisible] = useState(true);
  const [photo, setPhoto] = useState<PhotoDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadToken, setReloadToken] = useState(0);
  const [status, setStatus] = useState<ViewerStatus | null>(null);
  const [saveBusy, setSaveBusy] = useState(false);
  const [wallpaperBusy, setWallpaperBusy] = useState(false);
  const [photosCooldown, setPhotosCooldown] = useState(false);
  const [wallpaperCooldown, setWallpaperCooldown] = useState(false);
  const [wallpaperSheetVisible, setWallpaperSheetVisible] = useState(false);
  const photoRef = useRef<PhotoDetail | null>(null);
  const swipeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const statusTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const photosCooldownTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const wallpaperCooldownTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const saveBusyRef = useRef(false);
  const wallpaperBusyRef = useRef(false);
  const photosCooldownRef = useRef(false);
  const wallpaperCooldownRef = useRef(false);
  photoRef.current = photo;

  useEffect(() => {
    const controller = new AbortController();
    setError(null);
    setLoading(photoRef.current == null);
    fetchPhotoDetail(slug, picId, { size, signal: controller.signal })
      .then((detail) => {
        setPhoto(detail);
        setLoading(false);
        if (size && resolvedPhotoSize(size, detail.detailPath) !== size) {
          navigation.setParams({ size: '' });
        }
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        setPhoto(null);
        setError(err instanceof ApiError ? err.message : 'Something went wrong.');
        setLoading(false);
      });
    return () => controller.abort();
  }, [slug, picId, size, reloadToken, navigation]);

  useEffect(() => {
    return () => {
      clearTimeoutRef(swipeTimerRef);
      clearTimeoutRef(statusTimerRef);
      clearTimeoutRef(photosCooldownTimerRef);
      clearTimeoutRef(wallpaperCooldownTimerRef);
    };
  }, []);

  const retry = useCallback(() => setReloadToken((n) => n + 1), []);

  const goTo = useCallback(
    (neighborPicId: number) => {
      if (photoRef.current == null || photoRef.current.picId !== picId) {
        return;
      }

      navigation.setParams(photoViewerParams(slug, neighborPicId, size));
    },
    [navigation, picId, size, slug],
  );

  const display = photoViewerDisplay(photo, picId);
  const { previousPicId, nextPicId, image, neighbourUris } = display;
  const onCurrentLoaded = usePrefetchNeighbours(image?.uri, neighbourUris);

  const handleGallerySwipe = useCallback(
    (direction: 'previous' | 'next') => {
      const targetPicId = direction === 'previous' ? previousPicId : nextPicId;
      if (targetPicId == null) {
        return;
      }

      if (swipeTimerRef.current != null) {
        clearTimeout(swipeTimerRef.current);
      }

      swipeTimerRef.current = schedulePhotoGallerySwipe(() => goTo(targetPicId));
    },
    [goTo, nextPicId, previousPicId],
  );

  const toggleChrome = useCallback(() => {
    setChromeVisible((value) => !value);
  }, []);

  const showStatus = useCallback((message: string, kind: ViewerStatusKind) => {
    clearTimeoutRef(statusTimerRef);
    setStatus({ message, kind });
    AccessibilityInfo.announceForAccessibility(message);
    statusTimerRef.current = setTimeout(() => {
      setStatus(null);
      statusTimerRef.current = null;
    }, kind === 'success' ? photoViewerStatusTiming.successDismissMs : photoViewerStatusTiming.errorDismissMs);
  }, []);

  const startPhotosCooldown = useCallback(() => {
    clearTimeoutRef(photosCooldownTimerRef);
    photosCooldownRef.current = true;
    setPhotosCooldown(true);
    photosCooldownTimerRef.current = setTimeout(() => {
      photosCooldownRef.current = false;
      setPhotosCooldown(false);
      photosCooldownTimerRef.current = null;
    }, photoViewerStatusTiming.cooldownMs);
  }, []);

  const startWallpaperCooldown = useCallback(() => {
    clearTimeoutRef(wallpaperCooldownTimerRef);
    wallpaperCooldownRef.current = true;
    setWallpaperCooldown(true);
    wallpaperCooldownTimerRef.current = setTimeout(() => {
      wallpaperCooldownRef.current = false;
      setWallpaperCooldown(false);
      wallpaperCooldownTimerRef.current = null;
    }, photoViewerStatusTiming.cooldownMs);
  }, []);

  useEffect(() => {
    saveBusyRef.current = false;
    wallpaperBusyRef.current = false;
    photosCooldownRef.current = false;
    wallpaperCooldownRef.current = false;
    setSaveBusy(false);
    setWallpaperBusy(false);
    setPhotosCooldown(false);
    setWallpaperCooldown(false);
    setStatus(null);
    setWallpaperSheetVisible(false);
    clearTimeoutRef(statusTimerRef);
    clearTimeoutRef(photosCooldownTimerRef);
    clearTimeoutRef(wallpaperCooldownTimerRef);
  }, [picId]);

  useEffect(() => {
    if (!chromeVisible) {
      setWallpaperSheetVisible(false);
    }
  }, [chromeVisible]);

  const handleSave = useCallback(async () => {
    const current = photoRef.current;
    if (current == null || saveBusyRef.current || wallpaperBusyRef.current) {
      return;
    }

    if (photosCooldownRef.current) {
      showStatus(saveGalleryPhotoCopy.alreadySaved, 'success');
      return;
    }

    const startedPicId = current.picId;
    saveBusyRef.current = true;
    setSaveBusy(true);
    setStatus(null);
    try {
      await saveGalleryPhoto(current.imageUrl);
      if (photoRef.current?.picId !== startedPicId) {
        return;
      }
      showStatus(saveGalleryPhotoCopy.saved, 'success');
      startPhotosCooldown();
    } catch (err: unknown) {
      if (photoRef.current?.picId !== startedPicId) {
        return;
      }
      showStatus(err instanceof Error ? err.message : saveGalleryPhotoCopy.failed, 'error');
    } finally {
      saveBusyRef.current = false;
      if (photoRef.current?.picId === startedPicId) {
        setSaveBusy(false);
      }
    }
  }, [showStatus, startPhotosCooldown]);

  const handleWallpaper = useCallback(() => {
    const current = photoRef.current;
    if (current == null || wallpaperBusyRef.current || saveBusyRef.current) {
      return;
    }

    if (Platform.OS === 'android') {
      if (wallpaperCooldownRef.current) {
        showStatus(wallpaperCopy.androidAlreadySet, 'success');
        return;
      }
      setWallpaperSheetVisible((open) => !open);
      return;
    }

    if (Platform.OS !== 'ios') {
      return;
    }

    if (photosCooldownRef.current) {
      showStatus(wallpaperCopy.iosAlreadySaved, 'success');
      return;
    }

    const startedPicId = current.picId;
    wallpaperBusyRef.current = true;
    setWallpaperBusy(true);
    setStatus(null);
    void (async () => {
      try {
        await saveGalleryPhoto(current.imageUrl);
        if (photoRef.current?.picId !== startedPicId) {
          return;
        }
        showStatus(wallpaperCopy.iosSaved, 'success');
        startPhotosCooldown();
      } catch (err: unknown) {
        if (photoRef.current?.picId !== startedPicId) {
          return;
        }
        showStatus(err instanceof Error ? err.message : saveGalleryPhotoCopy.failed, 'error');
      } finally {
        wallpaperBusyRef.current = false;
        if (photoRef.current?.picId === startedPicId) {
          setWallpaperBusy(false);
        }
      }
    })();
  }, [showStatus, startPhotosCooldown]);

  const handleWallpaperTarget = useCallback(
    (target: WallpaperTarget) => {
      const current = photoRef.current;
      setWallpaperSheetVisible(false);
      if (current == null || wallpaperBusyRef.current || saveBusyRef.current) {
        return;
      }

      if (wallpaperCooldownRef.current) {
        showStatus(wallpaperCopy.androidAlreadySet, 'success');
        return;
      }

      const startedPicId = current.picId;
      wallpaperBusyRef.current = true;
      setWallpaperBusy(true);
      setStatus(null);
      void (async () => {
        try {
          await setAndroidGalleryWallpaper(current.imageUrl, target);
          if (photoRef.current?.picId !== startedPicId) {
            return;
          }
          showStatus(wallpaperCopy.androidSet, 'success');
          startWallpaperCooldown();
        } catch (err: unknown) {
          if (photoRef.current?.picId !== startedPicId) {
            return;
          }
          showStatus(err instanceof Error ? err.message : wallpaperCopy.bothFailed, 'error');
        } finally {
          wallpaperBusyRef.current = false;
          if (photoRef.current?.picId === startedPicId) {
            setWallpaperBusy(false);
          }
        }
      })();
    },
    [showStatus, startWallpaperCooldown],
  );

  if (loading && !photo) {
    return <LoadingBlock label="Loading photograph…" />;
  }

  if (error || !photo) {
    return <ErrorBlock message={error ?? 'Photograph not found.'} onRetry={retry} />;
  }


  return (
    <View testID={testIds.photoViewerScreen} style={{ flex: 1, backgroundColor: '#000' }}>
      <View style={{ flex: 1 }}>
        {image ? (
          <ZoomableArchiveImage
            source={image}
            label={photo.title}
            recyclingKey={display.recyclingKey}
            imageWidth={display.imageWidth}
            imageHeight={display.imageHeight}
            resetKey={display.resetKey!}
            canSwipePrevious={previousPicId != null}
            canSwipeNext={nextPicId != null}
            onGallerySwipe={handleGallerySwipe}
            onToggleChrome={toggleChrome}
            pending={display.pending}
            onLoaded={onCurrentLoaded}
          />
        ) : (
          <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center' }}>
            <Text style={[type.body, { color: c.textSecondary }]}>Image unavailable</Text>
          </View>
        )}
      </View>
      {chromeVisible ? (
        <PhotoViewerChrome photo={photo} hasImage={image != null} status={status}
          saveBusy={saveBusy} wallpaperBusy={wallpaperBusy} photosCooldown={photosCooldown} wallpaperCooldown={wallpaperCooldown}
          onClose={() => navigation.goBack()} onWallpaper={handleWallpaper} onSave={handleSave} goTo={goTo} />
      ) : null}
      {chromeVisible && wallpaperSheetVisible && Platform.OS === 'android' ? (
        <WallpaperTargetSheet
          onSelect={handleWallpaperTarget}
          onCancel={() => setWallpaperSheetVisible(false)}
        />
      ) : null}
    </View>
  );
}
