import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { Check, ChevronLeft, ChevronRight, Download, Wallpaper, X } from 'lucide-react-native';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, Platform, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { ApiError, fetchPhotoDetail, type PhotoDetail } from '../../api';
import type { PhotosStackParamList } from '../../navigation/types';
import { testIds } from '../../test/testIds';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { IconButton } from '../../ui/IconButton';
import { MetaLine } from '../../ui/MetaLine';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import {
  photoCdnSource,
  photoCounterLabel,
  photoDetailMeta,
  photoViewerParams,
  resolvedPhotoSize,
  schedulePhotoGallerySwipe,
} from './photoGalleryMeta';
import { saveGalleryPhoto, saveGalleryPhotoCopy } from './saveGalleryPhoto';
import { setAndroidGalleryWallpaper } from './setGalleryWallpaper';
import { WallpaperTargetSheet } from './WallpaperTargetSheet';
import { wallpaperCopy, type WallpaperTarget } from './wallpaperMeta';
import { ZoomableArchiveImage } from './ZoomableArchiveImage';

type Props = NativeStackScreenProps<PhotosStackParamList, 'PhotoViewer'>;
type ViewerStatusKind = 'success' | 'error';
type ViewerStatus = { message: string; kind: ViewerStatusKind };

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

function ViewerStatusBanner({
  status,
  top,
}: {
  status: ViewerStatus;
  top: number;
}) {
  const { c } = useTheme();
  return (
    <View
      testID={testIds.photoViewerStatus}
      pointerEvents="none"
      accessibilityLiveRegion="polite"
      style={{
        position: 'absolute',
        top,
        left: space.base,
        right: space.base,
        alignItems: 'center',
      }}
    >
      <View
        style={{
          maxWidth: '100%',
          paddingHorizontal: space.md,
          paddingVertical: space.sm,
          borderRadius: radius.pill,
          backgroundColor: status.kind === 'error' ? 'rgba(142,47,47,0.94)' : 'rgba(17,17,17,0.88)',
          borderWidth: 1,
          borderColor: status.kind === 'error' ? c.danger : c.accentPrimary,
        }}
      >
        <Text
          style={[
            type.caption,
            { color: '#FFFFFF', textAlign: 'center', fontFamily: fonts.bodyMedium },
          ]}
        >
          {status.message}
        </Text>
      </View>
    </View>
  );
}

export function PhotoViewerScreen({ navigation, route }: Props) {
  const insets = useSafeAreaInsets();
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

  const previousPicId = photo?.previous?.picId ?? null;
  const nextPicId = photo?.next?.picId ?? null;

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

  const image = photoCdnSource(photo.imageUrl);
  const actionBusy = saveBusy || wallpaperBusy;
  const saveIcon = photosCooldown && !saveBusy ? Check : Download;
  const wallpaperOnCooldown = Platform.OS === 'ios' ? photosCooldown : wallpaperCooldown;
  const wallpaperIcon = wallpaperOnCooldown && !wallpaperBusy ? Check : Wallpaper;

  return (
    <View testID={testIds.photoViewerScreen} style={{ flex: 1, backgroundColor: '#000' }}>
      <View style={{ flex: 1 }}>
        {image ? (
          <ZoomableArchiveImage
            source={image}
            label={photo.title}
            recyclingKey={`photo-full-${photo.picId}`}
            imageWidth={photo.pictureWidth}
            imageHeight={photo.pictureHeight}
            resetKey={photo.picId}
            canSwipePrevious={previousPicId != null}
            canSwipeNext={nextPicId != null}
            onGallerySwipe={handleGallerySwipe}
            onToggleChrome={toggleChrome}
          />
        ) : (
          <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center' }}>
            <Text style={[type.body, { color: c.textSecondary }]}>Image unavailable</Text>
          </View>
        )}
      </View>
      {chromeVisible ? (
        <View pointerEvents="box-none" style={StyleSheet.absoluteFill}>
          <View
            style={{
              position: 'absolute',
              top: insets.top,
              left: 4,
              right: 4,
              flexDirection: 'row',
              alignItems: 'center',
              justifyContent: 'space-between',
            }}
          >
            <View style={{ flex: 1, flexDirection: 'row', justifyContent: 'flex-start' }}>
              <IconButton
                icon={X}
                accessibilityLabel="Close"
                testID={testIds.photoViewerClose}
                onPress={() => navigation.goBack()}
              />
            </View>
            <Text style={[type.eyebrow, { color: c.textMuted }]}>
              {photoCounterLabel(photo.index, photo.count)}
            </Text>
            <View style={{ flex: 1, flexDirection: 'row', justifyContent: 'flex-end' }}>
              {image ? (
                <>
                  <IconButton
                    icon={wallpaperIcon}
                    accessibilityLabel={wallpaperCopy.accessibilityLabel}
                    testID={testIds.photoViewerWallpaper}
                    disabled={actionBusy}
                    busy={wallpaperBusy}
                    onPress={handleWallpaper}
                  />
                  <IconButton
                    icon={saveIcon}
                    accessibilityLabel="Save to Photos"
                    testID={testIds.photoViewerSave}
                    disabled={actionBusy}
                    busy={saveBusy}
                    onPress={() => {
                      void handleSave();
                    }}
                  />
                </>
              ) : (
                <View style={{ width: 44 }} />
              )}
            </View>
          </View>
          {status ? <ViewerStatusBanner status={status} top={insets.top + 48} /> : null}
          {photo.previous ? (
            <View style={{ position: 'absolute', left: 4, top: '45%' }}>
              <IconButton
                icon={ChevronLeft}
                accessibilityLabel="Previous image"
                onPress={() => goTo(photo.previous!.picId)}
              />
            </View>
          ) : null}
          {photo.next ? (
            <View style={{ position: 'absolute', right: 4, top: '45%' }}>
              <IconButton
                icon={ChevronRight}
                accessibilityLabel="Next image"
                onPress={() => goTo(photo.next!.picId)}
              />
            </View>
          ) : null}
          <View
            testID={testIds.photoViewerMeta}
            style={{
              position: 'absolute',
              left: 24,
              right: 24,
              bottom: insets.bottom + 24,
              gap: 8,
            }}
          >
            <Text style={[type.cardTitle, { color: c.textPrimary }]}>{photo.title}</Text>
            <MetaLine parts={photoDetailMeta(photo)} />
          </View>
        </View>
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
