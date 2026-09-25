import * as Sentry from '@sentry/react-native';
import { createMemoryStorage } from '../cache/storage';
import { resetExternalStoreForTests } from '../cache/externalStore';
import { fanPerformanceFixture } from '../test/fixtures';
import { createMemoryDownloadHost, getDownloadFileHost, setDownloadFileHostForTests } from './files';
import {
  getCompletedDownload,
  reconcileDownloadManifest,
  removeCompletedDownload,
  setDownloadManifestStorageForTests,
  upsertCompletedDownload,
} from './manifest';
import {
  enqueueDownload,
  purgeAllDownloads,
  removeDownload,
  resetDownloadManagerForTests,
  setDownloadProbeForTests,
} from './manager';
import {
  DOWNLOAD_EMPTY_PART_MESSAGE,
  DOWNLOAD_FAILED_MESSAGE,
  DOWNLOAD_INCOMPLETE_MESSAGE,
  DOWNLOAD_PART_MISSING_MESSAGE,
  DOWNLOAD_RATE_LIMITED_MESSAGE,
  DOWNLOAD_TOO_SMALL_MESSAGE,
  OFFLINE_PLAYBACK_MESSAGE,
  SIGN_IN_PLAYBACK_MESSAGE,
} from './messages';
import { resolveAudioSource } from './resolveAudioSource';
import { getDownloadUiSnapshot, resetDownloadUiForTests, setDownloadUiSnapshot, transientSnapshot } from './uiState';
import type { DownloadManifestEntry } from './types';

const memberId = 'member-1';
const track = fanPerformanceFixture();

function completed(overrides: Partial<DownloadManifestEntry> = {}): DownloadManifestEntry {
  return {
    performanceId: '187',
    localUri: 'file:///documents/fan-performances/187',
    title: track.title,
    performedBy: track.performedBy,
    byteSize: 4,
    sourceRevision: '"etag-1"',
    completedAt: '2026-09-05T00:00:00.000Z',
    memberId,
    ...overrides,
  };
}

function resetDownloads() {
  resetDownloadManagerForTests();
  resetDownloadUiForTests();
  resetExternalStoreForTests();
  setDownloadManifestStorageForTests(createMemoryStorage());
  setDownloadFileHostForTests(createMemoryDownloadHost());
  setDownloadProbeForTests(null);
}

describe('download file host URIs', () => {
  it('joins completed and part names onto the memory root without stacking slashes', () => {
    const host = createMemoryDownloadHost();
    expect(host.completedUri('perf-1', 'mp3')).toBe('file:///documents/fan-performances/perf-1.mp3');
    expect(host.partUri('perf-1')).toBe('file:///documents/fan-performances/perf-1.part');
  });
});

describe('download manifest reconciliation', () => {
  beforeEach(resetDownloads);
  afterEach(() => {
    setDownloadFileHostForTests(null);
    setDownloadManifestStorageForTests(null);
  });

  it('drops missing and zero-length files and scrubs orphan parts', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/188', new Uint8Array());
    host.files.set('file:///documents/fan-performances/189.part', new Uint8Array([1]));
    await upsertCompletedDownload(completed());
    await upsertCompletedDownload(
      completed({
        performanceId: '188',
        localUri: 'file:///documents/fan-performances/188',
      }),
    );

    const next = await reconcileDownloadManifest(memberId);
    expect(next.entries['187']).toBeUndefined();
    expect(next.entries['188']).toBeUndefined();
    expect(host.exists('file:///documents/fan-performances/189.part')).toBe(false);
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
  });

  it('migrates a valid extensionless download for iOS playback', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload(completed());

    const kept = await reconcileDownloadManifest(memberId);
    expect(kept.entries['187']?.sourceRevision).toBe('"etag-1"');
    expect(kept.entries['187']?.localUri).toBe('file:///documents/fan-performances/187.mp3');
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(host.exists('file:///documents/fan-performances/187.mp3')).toBe(true);
    await removeCompletedDownload(memberId, '187');
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
  });
});

describe('resolveAudioSource', () => {
  beforeEach(resetDownloads);
  afterEach(() => {
    setDownloadFileHostForTests(null);
    setDownloadManifestStorageForTests(null);
  });

  it('prefers a valid same-member local file without refreshing a token', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload(completed());
    const ensureAccessToken = jest.fn(async () => 'should-not-run');

    await expect(
      resolveAudioSource({
        track,
        memberId,
        ensureAccessToken,
        isOffline: true,
      }),
    ).resolves.toEqual({ kind: 'local', uri: 'file:///documents/fan-performances/187' });
    expect(ensureAccessToken).not.toHaveBeenCalled();
  });

  it('returns the offline-specific error when the recording is not downloaded', async () => {
    await expect(
      resolveAudioSource({
        track,
        memberId,
        ensureAccessToken: async () => 'token',
        isOffline: true,
      }),
    ).resolves.toEqual({ kind: 'error', message: OFFLINE_PLAYBACK_MESSAGE });
  });

  it('streams with Bearer when online and nothing is downloaded', async () => {
    const source = await resolveAudioSource({
      track,
      memberId,
      ensureAccessToken: async () => 'member-token',
      isOffline: false,
    });
    expect(source).toEqual({
      kind: 'stream',
      uri: expect.stringContaining('/content/fan-performances/187/audio'),
      headers: { Authorization: 'Bearer member-token' },
    });
    expect(JSON.stringify(source)).not.toContain('cdn');
    expect(JSON.stringify(source)).not.toContain('blob');
  });

  it('asks the member to sign in when there is no local file and no token', async () => {
    await expect(
      resolveAudioSource({
        track,
        memberId: null,
        ensureAccessToken: async () => null,
        isOffline: false,
      }),
    ).resolves.toEqual({ kind: 'error', message: SIGN_IN_PLAYBACK_MESSAGE });
  });

  it('discards a JSON error-page local file and streams instead', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set(
      'file:///documents/fan-performances/187',
      new TextEncoder().encode('{"title":"Unauthorized"}'),
    );
    await upsertCompletedDownload(completed({ byteSize: 24 }));

    const source = await resolveAudioSource({
      track,
      memberId,
      ensureAccessToken: async () => 'member-token',
      isOffline: false,
    });
    expect(source).toEqual({
      kind: 'stream',
      uri: expect.stringContaining('/content/fan-performances/187/audio'),
      headers: { Authorization: 'Bearer member-token' },
    });
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
  });
});

describe('download manager', () => {
  beforeEach(resetDownloads);
  afterEach(() => {
    jest.restoreAllMocks();
    setDownloadFileHostForTests(null);
    setDownloadManifestStorageForTests(null);
    setDownloadProbeForTests(null);
  });

  it('prevents duplicate downloads from repeated taps and stores the ETag as sourceRevision', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    enqueueDownload(track, memberId, async () => 'member-token');
    await Promise.resolve();
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(host.files.get('file:///documents/fan-performances/187.mp3')?.byteLength).toBe(4);
    expect(host.exists('file:///documents/fan-performances/187.part')).toBe(false);
    const stored = await getCompletedDownload(memberId, '187');
    expect(stored?.sourceRevision).toBe('"etag-9"');
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('keeps a FLAC extension for iOS local playback', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri }) => {
        host.files.set(destUri, new Uint8Array([0x66, 0x4c, 0x61, 0x43, 0x00]));
        return { uri: destUri };
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"flac-etag"',
      byteSize: 5,
      contentType: 'audio/flac',
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(host.exists('file:///documents/fan-performances/187.flac')).toBe(true);
    expect((await getCompletedDownload(memberId, '187'))?.localUri).toBe(
      'file:///documents/fan-performances/187.flac',
    );
  });

  it('waits for asynchronous file promotion before validating and storing the download', async () => {
    const host = createMemoryDownloadHost();
    const promoteNow = host.promote.bind(host);
    let finishPromote!: () => void;
    const promotionPending = new Promise<void>((resolve) => {
      finishPromote = resolve;
    });
    host.promote = jest.fn(async (partUri, completedUri) => {
      await promotionPending;
      promoteNow(partUri, completedUri);
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(host.promote).toHaveBeenCalled();
    expect(host.exists('file:///documents/fan-performances/187.mp3')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloading');

    finishPromote();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(host.files.get('file:///documents/fan-performances/187.mp3')?.byteLength).toBe(4);
    expect((await getCompletedDownload(memberId, '187'))?.byteSize).toBe(4);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('rejects a completed file that is an HTTP error payload', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri }) => {
        host.files.set(destUri, new TextEncoder().encode('{"title":"Unauthorized"}'));
        return { uri: destUri };
      },
    });
    const promote = jest.spyOn(host, 'promote');
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 24,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(promote).not.toHaveBeenCalled();
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('failed');
    expect(getDownloadUiSnapshot(memberId, '187')?.status).not.toBe('downloaded');
  });

  it('rejects an HTML error page saved as a tiny complete download', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri, onProgress }) => {
        const body = new TextEncoder().encode('<html><title>Too Many Requests</title></html>');
        onProgress?.(body.byteLength, body.byteLength);
        host.files.set(destUri, body);
        return { uri: destUri };
      },
    });
    const promote = jest.spyOn(host, 'promote');
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 0,
      sourceRevision: null,
      byteSize: null,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(promote).not.toHaveBeenCalled();
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_TOO_SMALL_MESSAGE,
    });
    expect(getDownloadUiSnapshot(memberId, '187')?.status).not.toBe('downloaded');
  });

  it('deletes the partial and does not complete after a failed download', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async () => {
        throw new Error('boom');
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187.part')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('failed');
  });

  it('rejects unauthorized recordings and low storage without a completed entry', async () => {
    const host = createMemoryDownloadHost({ availableBytes: 100 });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 404,
      sourceRevision: null,
      byteSize: null,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('failed');
  });

  it('removes the file and manifest entry without touching a server copy', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload(completed());

    await removeDownload(memberId, '187');
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(getDownloadUiSnapshot(memberId, '187')).toBeNull();
  });

  it('wires onProgress into the downloading snapshot', async () => {
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri, onProgress }) => {
        onProgress?.(256, 1024);
        await held;
        host.files.set(destUri, new Uint8Array(1024));
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 1024,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    const snapshot = getDownloadUiSnapshot(memberId, '187');
    expect(snapshot?.status).toBe('downloading');
    expect(snapshot?.byteSize).toBe(256);
    expect(snapshot?.expectedBytes).toBe(1024);

    release();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('writes progress onto the active performance id, not the first queued id', async () => {
    const first = fanPerformanceFixture({
      id: 191,
      title: 'Aaa First',
      detailPath: '/fan-performances/191',
      audioPath: '/api/v1/content/fan-performances/191/audio',
    });
    const third = fanPerformanceFixture({
      id: 193,
      title: 'Zzz Last',
      detailPath: '/fan-performances/193',
      audioPath: '/api/v1/content/fan-performances/193/audio',
    });
    setDownloadUiSnapshot(
      memberId,
      transientSnapshot('191', 'failed', {
        title: first.title,
        performedBy: first.performedBy,
        error: 'Could not download this recording. Try again.',
      }),
    );
    setDownloadUiSnapshot(
      memberId,
      transientSnapshot('192', 'failed', {
        title: 'Mmm Middle',
        performedBy: 'Mel',
        error: 'Could not download this recording. Try again.',
      }),
    );
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri, onProgress }) => {
        onProgress?.(400, 1000);
        await held;
        host.files.set(destUri, new Uint8Array(1000));
        return { uri: destUri };
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 1000,
    }));

    enqueueDownload(third, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(getDownloadUiSnapshot(memberId, '193')).toMatchObject({
      status: 'downloading',
      byteSize: 400,
      expectedBytes: 1000,
    });
    expect(getDownloadUiSnapshot(memberId, '191')?.status).toBe('failed');
    expect(getDownloadUiSnapshot(memberId, '191')?.byteSize).toBeNull();
    expect(getDownloadUiSnapshot(memberId, '192')?.status).toBe('failed');

    release();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getDownloadUiSnapshot(memberId, '193')?.status).toBe('downloaded');
    expect(getDownloadUiSnapshot(memberId, '191')?.status).toBe('failed');
  });

  it('still downloads when the Range probe times out or returns a non-206', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 0,
      sourceRevision: null,
      byteSize: null,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(host.files.get('file:///documents/fan-performances/187.mp3')?.byteLength).toBe(4);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('surfaces a 429 probe as a readable failed reason and stays retryable', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 429,
      sourceRevision: null,
      byteSize: null,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_RATE_LIMITED_MESSAGE,
    });

    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));
    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('retries a stale downloading snapshot that has no running job', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));
    setDownloadUiSnapshot(
      memberId,
      transientSnapshot('187', 'downloading', {
        title: track.title,
        performedBy: track.performedBy,
      }),
    );

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('treats a missing .part as a finalize failure and never promotes', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async () => ({ uri: 'file:///documents/fan-performances/187.part' }),
    });
    const promote = jest.spyOn(host, 'promote');
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(promote).not.toHaveBeenCalled();
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_PART_MISSING_MESSAGE,
    });
    expect(getDownloadUiSnapshot(memberId, '187')?.error).not.toBe(DOWNLOAD_FAILED_MESSAGE);
    expect(Sentry.addBreadcrumb).toHaveBeenCalledWith(
      expect.objectContaining({
        category: 'download',
        message: 'task-complete',
        data: expect.objectContaining({
          exists: false,
          size: 0,
          destMismatch: false,
          progressVsProbe: 'probe-only',
          tinyComplete: false,
        }),
      }),
    );
  });

  it('treats an empty .part as a finalize failure before promote', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri }) => {
        host.files.set(destUri, new Uint8Array());
        return { uri: destUri };
      },
    });
    const promote = jest.spyOn(host, 'promote');
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(promote).not.toHaveBeenCalled();
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187.part')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_EMPTY_PART_MESSAGE,
    });
    expect(getDownloadUiSnapshot(memberId, '187')?.error).not.toBe(DOWNLOAD_FAILED_MESSAGE);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).not.toBe('downloaded');
  });

  it('prefers the returned task File URI when dest .part was not written', async () => {
    const returnedUri = 'file:///cache/task-187';
    const host = createMemoryDownloadHost({
      downloadImpl: async () => {
        host.files.set(returnedUri, new Uint8Array([1, 2, 3, 4]));
        return { uri: returnedUri };
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(host.exists('file:///documents/fan-performances/187.mp3')).toBe(true);
    expect(host.exists(returnedUri)).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
    expect(Sentry.addBreadcrumb).toHaveBeenCalledWith(
      expect.objectContaining({
        category: 'download',
        message: 'task-complete',
        data: expect.objectContaining({
          destMismatch: true,
          destUri: 'file:///documents/fan-performances/187.part',
          returnedUri,
        }),
      }),
    );
  });

  it('breadcrumbs a Cloudflare hop host+path without query tokens', async () => {
    jest.mocked(Sentry.addBreadcrumb).mockClear();
    const returnedUri = 'file:///cache/task-187';
    const host = createMemoryDownloadHost({
      downloadImpl: async () => {
        host.files.set(returnedUri, new Uint8Array([1, 2, 3, 4]));
        return { uri: returnedUri };
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
      redirected: true,
      finalTarget: 'cdn2.queenzone.org/songfiles/clip.mp3',
      contentType: 'audio/mpeg',
      contentLength: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
    expect(Sentry.addBreadcrumb).toHaveBeenCalledWith(
      expect.objectContaining({
        category: 'download',
        message: 'probe',
        data: expect.objectContaining({
          redirected: true,
          finalTarget: 'cdn2.queenzone.org/songfiles/clip.mp3',
          requestTarget: expect.stringMatching(/\/api\/v1\/content\/fan-performances\/187\/audio$/),
        }),
      }),
    );
    expect(Sentry.addBreadcrumb).toHaveBeenCalledWith(
      expect.objectContaining({
        category: 'download',
        message: 'task-complete',
        data: expect.objectContaining({
          destMismatch: true,
          redirected: true,
          finalTarget: 'cdn2.queenzone.org/songfiles/clip.mp3',
        }),
      }),
    );
    const payload = JSON.stringify(jest.mocked(Sentry.addBreadcrumb).mock.calls);
    expect(payload).not.toMatch(/[?&](sig|token|access_token)=/i);
    expect(payload).not.toContain('member-token');
    expect(payload).not.toContain('Bearer');
  });

  it('rejects a tiny 100% progress total that does not match a real recording', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri, onProgress }) => {
        onProgress?.(200, 200);
        host.files.set(destUri, new Uint8Array(200));
        return { uri: destUri };
      },
    });
    const promote = jest.spyOn(host, 'promote');
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 0,
      sourceRevision: null,
      byteSize: null,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(promote).not.toHaveBeenCalled();
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_TOO_SMALL_MESSAGE,
    });
    expect(getDownloadUiSnapshot(memberId, '187')?.status).not.toBe('downloaded');
    expect(Sentry.addBreadcrumb).toHaveBeenCalledWith(
      expect.objectContaining({
        category: 'download',
        message: 'task-complete',
        data: expect.objectContaining({
          progressTotal: 200,
          size: 200,
          progressVsProbe: 'progress-only',
          tinyComplete: true,
          destMismatch: false,
        }),
      }),
    );
  });

  it('does not let a tiny progress total overwrite a larger probe Content-Length', async () => {
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri, onProgress }) => {
        onProgress?.(200, 200);
        await held;
        host.files.set(destUri, new Uint8Array(5_000_000));
        return { uri: destUri };
      },
    });
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 5_000_000,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));
    const snapshot = getDownloadUiSnapshot(memberId, '187');
    expect(snapshot?.status).toBe('downloading');
    expect(snapshot?.byteSize).toBe(200);
    expect(snapshot?.expectedBytes).toBe(5_000_000);

    release();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getDownloadUiSnapshot(memberId, '187')?.status).toBe('downloaded');
  });

  it('maps a NoSuchFile promote error to the missing-part message', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri }) => {
        host.files.set(destUri, new Uint8Array([1, 2, 3, 4]));
        return { uri: destUri };
      },
    });
    host.promote = () => {
      const error = new Error('NoSuchFileException: fan-performances/187.part');
      error.name = 'NoSuchFileException';
      throw error;
    };
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 4,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_PART_MISSING_MESSAGE,
    });
    expect(getDownloadUiSnapshot(memberId, '187')?.error).not.toBe(DOWNLOAD_FAILED_MESSAGE);
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
  });

  it('keeps rate-limit copy on a 5-minute window', async () => {
    expect(DOWNLOAD_RATE_LIMITED_MESSAGE).toContain('5 minutes');
    expect(DOWNLOAD_RATE_LIMITED_MESSAGE.toLowerCase()).not.toContain('wait a minute');
  });

  it('memory host promote refuses a missing or empty .part', () => {
    const host = createMemoryDownloadHost();
    expect(() => host.promote('file:///documents/fan-performances/187.part', 'file:///done')).toThrow(
      DOWNLOAD_PART_MISSING_MESSAGE,
    );
    host.files.set('file:///documents/fan-performances/187.part', new Uint8Array());
    expect(() =>
      host.promote('file:///documents/fan-performances/187.part', 'file:///done'),
    ).toThrow(DOWNLOAD_EMPTY_PART_MESSAGE);
  });

  it('rejects a short file against a larger probe Content-Length as incomplete', async () => {
    const host = createMemoryDownloadHost({
      downloadImpl: async ({ destUri }) => {
        host.files.set(destUri, new Uint8Array(1000));
        return { uri: destUri };
      },
    });
    const promote = jest.spyOn(host, 'promote');
    setDownloadFileHostForTests(host);
    setDownloadProbeForTests(async () => ({
      status: 206,
      sourceRevision: '"etag-9"',
      byteSize: 10_000,
    }));

    enqueueDownload(track, memberId, async () => 'member-token');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(promote).not.toHaveBeenCalled();
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(getDownloadUiSnapshot(memberId, '187')).toMatchObject({
      status: 'failed',
      error: DOWNLOAD_INCOMPLETE_MESSAGE,
    });
    expect(getDownloadUiSnapshot(memberId, '187')?.status).not.toBe('downloaded');
  });

  it('native host uses the completed one-shot File URI and refuses a missing .part move', async () => {
    const expoFileSystem = jest.requireMock('expo-file-system') as typeof import('expo-file-system');
    const { File } = expoFileSystem;
    const oneShot = jest.spyOn(File, 'downloadFileAsync');
    const task = jest.spyOn(File, 'createDownloadTask');
    setDownloadFileHostForTests(null);
    const host = getDownloadFileHost();
    const result = await host.download({
      url: 'https://example.test/audio',
      destUri: 'file:///documents/fan-performances/178.part',
      headers: { Authorization: 'Bearer token' },
    });
    expect(result?.uri).toBe('file:///documents/x');
    expect(oneShot).toHaveBeenCalledWith(
      'https://example.test/audio',
      expect.objectContaining({ uri: 'file:///documents/fan-performances/178.part' }),
      expect.objectContaining({
        headers: { Authorization: 'Bearer token' },
        idempotent: true,
      }),
    );
    expect(task).not.toHaveBeenCalled();
    expect(() => host.promote('file:///missing.part', 'file:///done')).toThrow(
      DOWNLOAD_PART_MISSING_MESSAGE,
    );

    let finishMove!: () => void;
    const movePending = new Promise<void>((resolve) => {
      finishMove = resolve;
    });
    const move = jest.fn(() => movePending);
    jest.spyOn(expoFileSystem, 'File').mockImplementation((...parts: unknown[]) => {
      const uri = parts.map(String).join('/');
      return {
        uri,
        exists: uri.endsWith('.part'),
        size: 4,
        delete: jest.fn(),
        move,
      } as unknown as InstanceType<typeof File>;
    });

    const promotion = host.promote(
      'file:///documents/fan-performances/178.part',
      'file:///documents/fan-performances/178',
    );
    expect(move).toHaveBeenCalledWith(expect.objectContaining({
      uri: 'file:///documents/fan-performances/178',
    }));
    finishMove();
    await promotion;
  });

  it('sign-out deletes files, partials, and the manifest', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    host.files.set('file:///documents/fan-performances/188.part', new Uint8Array([9]));
    await upsertCompletedDownload(completed());

    await purgeAllDownloads(memberId);
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
    expect(host.exists('file:///documents/fan-performances/188.part')).toBe(false);
    expect(await getCompletedDownload(memberId, '187')).toBeNull();
  });
});
