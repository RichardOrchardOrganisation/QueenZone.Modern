import { Pressable, StyleSheet, Text } from 'react-native';
import { Download, Check, CircleAlert, LoaderCircle } from 'lucide-react-native';
import type { FanPerformance } from '../api';
import { useSession } from '../session/SessionContext';
import { testIds } from '../test/testIds';
import { space, type, useTheme } from '../theme';
import { formatByteSize, formatDownloadProgress } from './formatBytes';
import { enqueueDownload, removeDownload } from './manager';
import { useDownloadMemberId, useDownloadUi } from './useDownloadUi';

type Props = {
  track: FanPerformance;
  compact?: boolean;
  onNeedSignIn?: () => void;
};

export function downloadStatusLabel(
  status: string | undefined,
  title: string,
  sizeLabel: string,
  error?: string | null,
): string {
  switch (status) {
    case 'queued':
      return `Download queued for ${title}`;
    case 'downloading':
      return sizeLabel ? `Downloading ${title}, ${sizeLabel}` : `Downloading ${title}`;
    case 'downloaded':
      return sizeLabel ? `${title} downloaded, ${sizeLabel}` : `${title} downloaded`;
    case 'failed':
      return error
        ? `Download failed for ${title}: ${error} Double tap to retry`
        : `Download failed for ${title}. Double tap to retry`;
    case 'removing':
      return `Removing download of ${title}`;
    default:
      return `Download ${title} for offline playback`;
  }
}

export function DownloadAction({ track, compact = false, onNeedSignIn }: Props) {
  const { c } = useTheme();
  const { isRestoring, ensureAccessToken } = useSession();
  const memberId = useDownloadMemberId();
  const performanceId = String(track.id);
  const snapshot = useDownloadUi(performanceId);
  const status = snapshot?.status;
  const progressLabel = formatDownloadProgress(snapshot?.byteSize, snapshot?.expectedBytes);
  const sizeLabel =
    status === 'downloading' ? progressLabel : formatByteSize(snapshot?.byteSize ?? snapshot?.expectedBytes);
  const error = snapshot?.error;
  const label = downloadStatusLabel(status, track.title, sizeLabel, error);

  const onPress = () => {
    if (isRestoring) {
      return;
    }
    if (!memberId) {
      onNeedSignIn?.();
      return;
    }
    if (status === 'downloaded') {
      void removeDownload(memberId, performanceId);
      return;
    }
    if (status === 'queued' || status === 'downloading' || status === 'removing') {
      return;
    }
    enqueueDownload(track, memberId, ensureAccessToken);
  };

  const Icon =
    status === 'downloaded'
      ? Check
      : status === 'failed'
        ? CircleAlert
        : status === 'queued' || status === 'downloading' || status === 'removing'
          ? LoaderCircle
          : Download;

  const caption =
    status === 'downloaded'
      ? sizeLabel
        ? `Downloaded · ${sizeLabel}`
        : 'Downloaded'
      : status === 'downloading'
        ? sizeLabel
          ? `Downloading · ${sizeLabel}`
          : 'Downloading'
        : status === 'queued'
          ? 'Queued'
          : status === 'failed'
            ? error ?? 'Retry download'
            : status === 'removing'
              ? 'Removing'
              : 'Download';

  const showCaption = !compact || status === 'downloading' || status === 'failed';

  return (
    <Pressable
      testID={`${testIds.fanPerformanceDownloadPrefix}${performanceId}`}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint={
        status === 'downloaded' ? 'Removes the downloaded recording from this device' : undefined
      }
      accessibilityState={{
        busy: status === 'downloading' || status === 'removing' || status === 'queued',
        disabled: status === 'queued' || status === 'downloading' || status === 'removing',
      }}
      hitSlop={compact ? { top: 8, bottom: 8, left: 4, right: 8 } : 8}
      unstable_pressDelay={0}
      onPress={onPress}
      style={[
        styles.button,
        compact ? styles.compact : null,
        compact && showCaption ? (status === 'failed' ? styles.compactFailed : styles.compactWide) : null,
        { borderColor: c.borderStrong, backgroundColor: c.surfaceRaised },
      ]}
    >
      <Icon
        size={18}
        color={status === 'failed' ? c.danger : status === 'downloaded' ? c.accentPrimary : c.textPrimary}
      />
      {showCaption ? (
        <Text
          style={[
            compact && status !== 'failed' ? type.meta : type.caption,
            { color: status === 'failed' ? c.danger : c.textPrimary, flexShrink: 1 },
          ]}
          numberOfLines={status === 'failed' ? undefined : compact ? 2 : 3}
        >
          {compact && status === 'downloading' ? sizeLabel || '…' : caption}
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
