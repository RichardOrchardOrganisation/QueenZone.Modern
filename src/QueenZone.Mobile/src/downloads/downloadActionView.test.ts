import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { downloadActionView, downloadStatusLabel } from './downloadActionView.ts';
import type { DownloadUiSnapshot, DownloadUiStatus } from './types';

const statuses: (DownloadUiStatus | undefined)[] = [undefined, 'queued', 'downloading', 'downloaded', 'failed', 'removing'];
const expected = {
  idle: ['download', 'text', 'Download', false],
  queued: ['loader', 'text', 'Queued', true],
  downloading: ['loader', 'text', 'Downloading · 25%', true],
  downloaded: ['check', 'accent', 'Downloaded · 256 B', false],
  failed: ['alert', 'danger', 'Network unavailable', false],
  removing: ['loader', 'text', 'Removing', true],
} as const;
function snapshot(status: DownloadUiStatus): DownloadUiSnapshot {
  return { status, performanceId: '187', title: 'Song', performedBy: 'Fan', byteSize: 256, expectedBytes: 1024, error: 'Network unavailable' };
}

describe('download status to view and action', () => {
  for (const status of statuses) {
    for (const compact of [false, true]) {
      for (const restoring of [false, true]) {
        for (const signedIn of [false, true]) {
          it(`${status ?? 'idle'} compact=${compact} restoring=${restoring} signedIn=${signedIn}`, () => {
            const view = downloadActionView('Song', status ? snapshot(status) : null, compact, restoring, signedIn);
            const [icon, tint, caption, busy] = expected[status ?? 'idle'];
            assert.equal(view.icon, icon); assert.equal(view.tint, tint);
            assert.equal(view.caption, caption); assert.equal(view.busy, busy);
            const action = restoring ? 'none' : !signedIn ? 'sign-in' : status === 'downloaded' ? 'remove' : busy ? 'none' : 'enqueue';
            assert.equal(view.action, action);
            assert.equal(view.showCaption, !compact || status === 'downloading' || status === 'failed');
            assert.equal(view.captionText, compact && status === 'downloading' ? '25%' : caption);
            assert.equal(view.captionLines, status === 'failed' ? undefined : compact ? 2 : 3);
            assert.equal(view.captionStyle, compact && status !== 'failed' ? 'meta' : 'caption');
            assert.equal(view.captionTint, status === 'failed' ? 'danger' : 'text');
            assert.equal(view.hint, status === 'downloaded' ? 'Removes the downloaded recording from this device' : undefined);
            assert.equal(view.compactVariant, compact && view.showCaption ? status === 'failed' ? 'failed' : 'wide' : undefined);
            const labels = {
              idle: 'Download Song for offline playback', queued: 'Download queued for Song',
              downloading: 'Downloading Song, 25%', downloaded: 'Song downloaded, 256 B',
              failed: 'Download failed for Song: Network unavailable Double tap to retry', removing: 'Removing download of Song',
            };
            assert.equal(view.label, labels[status ?? 'idle']);
          });
        }
      }
    }
  }

  it('keeps size and failure fallbacks, including deliberately empty errors', () => {
    for (const status of ['downloading', 'downloaded', 'failed'] as const) {
      const noMetadata = { ...snapshot(status), byteSize: null, expectedBytes: null, error: null };
      const view = downloadActionView('Song', noMetadata, false, false, true);
      assert.equal(view.caption, status === 'failed' ? 'Retry download' : status === 'downloaded' ? 'Downloaded' : 'Downloading');
      assert.equal(view.label, status === 'failed' ? 'Download failed for Song. Double tap to retry' : status === 'downloaded' ? 'Song downloaded' : 'Downloading Song');
    }
    assert.equal(downloadActionView('Song', { ...snapshot('downloading'), byteSize: null, expectedBytes: null }, true, false, true).captionText, '…');
    assert.equal(downloadActionView('Song', { ...snapshot('failed'), error: '' }, true, false, true).captionText, '');
    assert.equal(downloadActionView('Song', { ...snapshot('downloaded'), byteSize: null, expectedBytes: 1024 }, false, false, true).caption, 'Downloaded · 1.0 KB');
    assert.equal(downloadActionView('Song', undefined, false, false, true).caption, 'Download');
    assert.equal(downloadStatusLabel('unknown', 'Song', ''), 'Download Song for offline playback');
  });
});
