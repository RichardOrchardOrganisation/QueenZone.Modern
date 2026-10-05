import { formatByteSize, formatDownloadProgress } from './formatBytes.ts';
import type { DownloadUiSnapshot } from './types';

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

export type DownloadPressAction = 'none' | 'sign-in' | 'remove' | 'enqueue';
export function downloadPressAction(status: string | undefined, restoring: boolean, signedIn: boolean): DownloadPressAction {
  if (restoring) return 'none';
  if (!signedIn) return 'sign-in';
  if (status === 'downloaded') return 'remove';
  if (status === 'queued' || status === 'downloading' || status === 'removing') return 'none';
  return 'enqueue';
}

function statusView(status: string | undefined, size: string, error: string | null | undefined) {
  switch (status) {
    case 'downloaded': return { icon: 'check' as const, tint: 'accent' as const, caption: size ? `Downloaded · ${size}` : 'Downloaded', busy: false };
    case 'downloading': return { icon: 'loader' as const, tint: 'text' as const, caption: size ? `Downloading · ${size}` : 'Downloading', busy: true };
    case 'queued': return { icon: 'loader' as const, tint: 'text' as const, caption: 'Queued', busy: true };
    case 'failed': return { icon: 'alert' as const, tint: 'danger' as const, caption: error ?? 'Retry download', busy: false };
    case 'removing': return { icon: 'loader' as const, tint: 'text' as const, caption: 'Removing', busy: true };
    default: return { icon: 'download' as const, tint: 'text' as const, caption: 'Download', busy: false };
  }
}

export function downloadActionView(title: string, snapshot: DownloadUiSnapshot | null | undefined, compact: boolean, restoring: boolean, signedIn: boolean) {
  const status = snapshot?.status;
  const sizeLabel = status === 'downloading' ? formatDownloadProgress(snapshot?.byteSize, snapshot?.expectedBytes)
    : formatByteSize(snapshot?.byteSize ?? snapshot?.expectedBytes);
  const view = statusView(status, sizeLabel, snapshot?.error);
  const failed = status === 'failed';
  const downloading = status === 'downloading';
  const showCaption = !compact || downloading || failed;
  const captionLineLimit = compact ? 2 : 3;
  const compactCaptionVariant = failed ? 'failed' as const : 'wide' as const;
  return {
    ...view,
    label: downloadStatusLabel(status, title, sizeLabel, snapshot?.error),
    action: downloadPressAction(status, restoring, signedIn),
    hint: status === 'downloaded' ? 'Removes the downloaded recording from this device' : undefined,
    showCaption,
    captionText: compact && downloading ? sizeLabel || '…' : view.caption,
    captionStyle: compact && !failed ? 'meta' as const : 'caption' as const,
    captionTint: failed ? 'danger' as const : 'text' as const,
    captionLines: failed ? undefined : captionLineLimit,
    compactVariant: compact && showCaption ? compactCaptionVariant : undefined,
  };
}
