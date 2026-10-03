import { Pressable, StyleSheet, Text } from 'react-native';
import { Download, Check, CircleAlert, LoaderCircle } from 'lucide-react-native';
import type { FanPerformance } from '../api';
import { useSession } from '../session/SessionContext';
import { testIds } from '../test/testIds';
import { space, type, useTheme } from '../theme';
import { downloadActionView } from './downloadActionView';
import { enqueueDownload, removeDownload } from './manager';
import { useDownloadMemberId, useDownloadUi } from './useDownloadUi';

type Props = {
  track: FanPerformance;
  compact?: boolean;
  onNeedSignIn?: () => void;
};

export { downloadStatusLabel } from './downloadActionView';

export function DownloadAction({ track, compact = false, onNeedSignIn }: Props) {
  const { c } = useTheme();
  const { isRestoring, ensureAccessToken } = useSession();
  const memberId = useDownloadMemberId();
  const performanceId = String(track.id);
  const snapshot = useDownloadUi(performanceId);
  const view = downloadActionView(track.title, snapshot, compact, isRestoring, Boolean(memberId));
  const Icon = { check: Check, alert: CircleAlert, loader: LoaderCircle, download: Download }[view.icon];
  const tint = { danger: c.danger, accent: c.accentPrimary, text: c.textPrimary };
  const onPress = () => {
    switch (view.action) {
      case 'sign-in': onNeedSignIn?.(); return;
      case 'remove': void removeDownload(memberId!, performanceId); return;
      case 'enqueue': enqueueDownload(track, memberId!, ensureAccessToken); return;
      case 'none': return;
    }
  };

  return (
    <Pressable
      testID={`${testIds.fanPerformanceDownloadPrefix}${performanceId}`}
      accessibilityRole="button"
      accessibilityLabel={view.label}
      accessibilityHint={view.hint}
      accessibilityState={{ busy: view.busy, disabled: view.busy }}
      hitSlop={compact ? { top: 8, bottom: 8, left: 4, right: 8 } : 8}
      unstable_pressDelay={0}
      onPress={onPress}
      style={[
        styles.button,
        compact ? styles.compact : null,
        view.compactVariant ? { failed: styles.compactFailed, wide: styles.compactWide }[view.compactVariant] : null,
        { borderColor: c.borderStrong, backgroundColor: c.surfaceRaised },
      ]}
    >
      <Icon size={18} color={tint[view.tint]} />
      {view.showCaption ? (
        <Text
          style={[type[view.captionStyle], { color: tint[view.captionTint], flexShrink: 1 }]}
          numberOfLines={view.captionLines}
        >
          {view.captionText}
        </Text>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 40,
    minWidth: 40,
    paddingHorizontal: space.md,
    borderRadius: 20,
    borderWidth: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
  },
  compact: {
    width: 40,
    minHeight: 40,
    paddingHorizontal: 0,
    justifyContent: 'center',
  },
  compactWide: {
    width: undefined,
    maxWidth: 96,
    minHeight: 40,
    paddingHorizontal: space.sm,
  },
  compactFailed: {
    width: undefined,
    maxWidth: 220,
    minHeight: 40,
    paddingHorizontal: space.sm,
    paddingVertical: space.xs,
    alignItems: 'flex-start',
  },
});
