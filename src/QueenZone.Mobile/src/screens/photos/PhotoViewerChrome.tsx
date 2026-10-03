import { Check, ChevronLeft, ChevronRight, Download, Wallpaper, X } from 'lucide-react-native';
import { Platform, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import type { PhotoDetail } from '../../api';
import { testIds } from '../../test/testIds';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { IconButton } from '../../ui/IconButton';
import { MetaLine } from '../../ui/MetaLine';
import { photoCounterLabel, photoDetailMeta } from './photoGalleryMeta';
import { photoViewerActions } from './photoViewerDisplay';
import { wallpaperCopy } from './wallpaperMeta';

export type ViewerStatusKind = 'success' | 'error';
export type ViewerStatus = { message: string; kind: ViewerStatusKind };

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

type Props = {
  photo: PhotoDetail; hasImage: boolean; status: ViewerStatus | null;
  saveBusy: boolean; wallpaperBusy: boolean; photosCooldown: boolean; wallpaperCooldown: boolean;
  onClose: () => void; onWallpaper: () => void; onSave: () => Promise<void>; goTo: (id: number) => void;
};

export function PhotoViewerChrome({ photo, hasImage, status, saveBusy, wallpaperBusy, photosCooldown, wallpaperCooldown, onClose, onWallpaper, onSave, goTo }: Props) {
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const actions = photoViewerActions(Platform.OS, photosCooldown, wallpaperCooldown, saveBusy, wallpaperBusy);
  const actionBusy = actions.busy;
  const saveIcon = actions.saveComplete ? Check : Download;
  const wallpaperIcon = actions.wallpaperComplete ? Check : Wallpaper;
  return (
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
                onPress={() => onClose()}
              />
            </View>
            <Text style={[type.eyebrow, { color: c.textMuted }]}>
              {photoCounterLabel(photo.index, photo.count)}
            </Text>
            <View style={{ flex: 1, flexDirection: 'row', justifyContent: 'flex-end' }}>
              {hasImage ? (
                <>
                  <IconButton
                    icon={wallpaperIcon}
                    accessibilityLabel={wallpaperCopy.accessibilityLabel}
                    testID={testIds.photoViewerWallpaper}
                    disabled={actionBusy}
                    busy={wallpaperBusy}
                    onPress={onWallpaper}
                  />
                  <IconButton
                    icon={saveIcon}
                    accessibilityLabel="Save to Photos"
                    testID={testIds.photoViewerSave}
                    disabled={actionBusy}
                    busy={saveBusy}
                    onPress={() => {
                      void onSave();
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
  );
}
