import { Linking, Share } from 'react-native';
import * as Sharing from 'expo-sharing';
import { saveToLibraryAsync } from 'expo-media-library/legacy';
import { writeAsStringAsync } from 'expo-file-system/legacy';
import { File } from 'expo-file-system';
import {
  DOWNLOAD_TIMED_OUT,
  FORUM_ATTACHMENT_TIMEOUT_MS,
  cacheForumAttachment,
  isCookieGatedForumAttachmentPath,
  openForumAttachmentFile,
  openForumAttachmentImage,
  resetForumAttachmentCacheForTests,
  saveForumAttachmentImage,
} from './forumAttachment';

jest.mock('../config', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'http://qz.test' }),
  apiV1Url: (path: string) => `http://qz.test/api/v1${path.startsWith('/') ? path : `/${path}`}`,
}));

type TaskCall = { url: string; destination: { uri: string }; options: { headers?: Record<string, string>; signal?: AbortSignal } };

const fetchMock = jest.fn();
const shareAsync = Sharing.shareAsync as jest.MockedFunction<typeof Sharing.shareAsync>;
const isAvailableAsync = Sharing.isAvailableAsync as jest.MockedFunction<
  typeof Sharing.isAvailableAsync
>;
const saveToLibrary = saveToLibraryAsync as jest.MockedFunction<typeof saveToLibraryAsync>;
const writeAsString = writeAsStringAsync as jest.MockedFunction<typeof writeAsStringAsync>;
let taskCalls: TaskCall[] = [];
let downloadImpl: (call: TaskCall) => Promise<{ uri: string } | null>;

function succeed(call: TaskCall) {
  return Promise.resolve({ uri: call.destination.uri });
}

/** Never settles unless the task signal aborts (models the iOS stall). */
function hang(call: TaskCall) {
  return new Promise<{ uri: string } | null>((_, reject) => {
    call.options.signal?.addEventListener('abort', () => reject(new Error('AbortError')));
  });
}

beforeEach(() => {
  resetForumAttachmentCacheForTests();
  taskCalls = [];
  downloadImpl = succeed;
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  jest
    .spyOn(File, 'createDownloadTask')
    .mockImplementation((url: string, destination: unknown, options?: unknown) => {
      const call = { url, destination, options: options ?? {} } as TaskCall;
      taskCalls.push(call);
      return { downloadAsync: () => downloadImpl(call) } as unknown as ReturnType<
        typeof File.createDownloadTask
      >;
    });
  jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
  jest.spyOn(Share, 'share').mockResolvedValue({ action: Share.sharedAction, activityType: undefined });
  isAvailableAsync.mockResolvedValue(true);
  shareAsync.mockResolvedValue(undefined);
  saveToLibrary.mockResolvedValue(undefined);
  writeAsString.mockResolvedValue(undefined);
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

describe('isCookieGatedForumAttachmentPath', () => {
  it('rejects the website cookie path and allows the Bearer alias', () => {
    expect(isCookieGatedForumAttachmentPath('/forum/attachment/legacy/1002')).toBe(true);
    expect(
      isCookieGatedForumAttachmentPath('/forum/attachment/9001/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),
    ).toBe(true);
    expect(isCookieGatedForumAttachmentPath('/api/v1/forum/attachments/legacy/1002')).toBe(false);
    expect(isCookieGatedForumAttachmentPath('https://cdn2.queenzone.org/attachments/scan.jpg')).toBe(
      false,
    );
  });
});

describe('cacheForumAttachment', () => {
  it('downloads straight to a cache file with Bearer and no fetch/base64 round-trip', async () => {
    const cached = await cacheForumAttachment(
      '/api/v1/forum/attachments/legacy/1201-cache',
      'tok',
      'brighton-rock-solo.mp3',
    );
    expect(taskCalls).toHaveLength(1);
    expect(taskCalls[0]?.url).toBe('http://qz.test/api/v1/forum/attachments/legacy/1201-cache');
    expect(taskCalls[0]?.options.headers).toEqual(
      expect.objectContaining({ Authorization: 'Bearer tok' }),
    );
    expect(cached.fileUri).toMatch(/^file:\/\/\/cache\/forum-attachments\/\d+\/brighton-rock-solo\.mp3$/);
    expect(cached.contentType).toBe('audio/mpeg');
    expect(fetchMock).not.toHaveBeenCalled();
    expect(writeAsString).not.toHaveBeenCalled();
  });

  it('reuses the cached file for the same attachment', async () => {
    const first = await cacheForumAttachment('/api/v1/forum/attachments/legacy/1', 'tok', 'a.pdf');
    const second = await cacheForumAttachment('/api/v1/forum/attachments/legacy/1', 'tok', 'a.pdf');
    expect(second).toEqual(first);
    expect(taskCalls).toHaveLength(1);
  });

  it('times out a stalled download, aborts it, and succeeds on the retry', async () => {
    jest.useFakeTimers();
    let attempt = 0;
    downloadImpl = (call) => {
      attempt += 1;
      return attempt === 1 ? hang(call) : succeed(call);
    };

    const pending = cacheForumAttachment('/api/v1/forum/attachments/legacy/2', 'tok', 'notes.pdf');
    await jest.advanceTimersByTimeAsync(FORUM_ATTACHMENT_TIMEOUT_MS);
    const cached = await pending;

    expect(taskCalls).toHaveLength(2);
    expect(taskCalls[0]?.options.signal?.aborted).toBe(true);
    expect(cached.fileUri).toMatch(/notes\.pdf$/);
    expect(cached.contentType).toBe('application/pdf');
  });

  it('shows a retryable timeout error after both attempts stall', async () => {
    jest.useFakeTimers();
    downloadImpl = hang;

    const pending = cacheForumAttachment('/api/v1/forum/attachments/legacy/3', 'tok', 'notes.pdf');
    const assertion = expect(pending).rejects.toMatchObject({
      name: 'ApiError',
      status: 408,
      message: DOWNLOAD_TIMED_OUT,
    });
    await jest.advanceTimersByTimeAsync(FORUM_ATTACHMENT_TIMEOUT_MS * 2);
    await assertion;
    expect(taskCalls).toHaveLength(2);

    // Tapping again starts a fresh download (nothing poisoned in the cache).
    jest.useRealTimers();
    downloadImpl = succeed;
    await expect(
      cacheForumAttachment('/api/v1/forum/attachments/legacy/3', 'tok', 'notes.pdf'),
    ).resolves.toMatchObject({ contentType: 'application/pdf' });
    expect(taskCalls).toHaveLength(3);
  });

  it('retries once after a network error', async () => {
    let attempt = 0;
    downloadImpl = (call) => {
      attempt += 1;
      return attempt === 1 ? Promise.reject(new Error('The network connection was lost.')) : succeed(call);
    };
    await expect(
      cacheForumAttachment('/api/v1/forum/attachments/legacy/4', 'tok', 'notes.txt'),
    ).resolves.toMatchObject({ contentType: 'text/plain' });
    expect(taskCalls).toHaveLength(2);
  });

  it('maps a 401 download to a sign-in error without retrying', async () => {
    downloadImpl = () => Promise.reject(new Error('Unable to download file: response has status 401'));
    await expect(
      cacheForumAttachment('/api/v1/forum/attachments/legacy/5', 'tok', 'notes.txt'),
    ).rejects.toMatchObject({ name: 'ApiError', status: 401, message: 'Sign in to continue.' });
    expect(taskCalls).toHaveLength(1);
  });
});

describe('openForumAttachmentFile', () => {
  it('shares the downloaded file:// URI and does not open the CDN', async () => {
    await openForumAttachmentFile('/api/v1/forum/attachments/legacy/1101-share', 'tok', 'notes.pdf');
    expect(shareAsync).toHaveBeenCalledWith(
      expect.stringMatching(/^file:\/\/\/cache\/forum-attachments\/\d+\/notes\.pdf$/),
      expect.objectContaining({ mimeType: 'application/pdf', dialogTitle: 'notes.pdf' }),
    );
    expect(Linking.openURL).not.toHaveBeenCalled();
    expect(Share.share).not.toHaveBeenCalled();
  });

  it('skips the Files sheet when present is false', async () => {
    await openForumAttachmentFile('/api/v1/forum/attachments/9001/bbbb', 'tok', 'attach.txt', {
      present: false,
    });
    expect(taskCalls).toHaveLength(1);
    expect(shareAsync).not.toHaveBeenCalled();
  });

  it('does not treat a share cancel as an error', async () => {
    shareAsync.mockRejectedValueOnce(new Error('User cancelled sharing'));
    await expect(
      openForumAttachmentFile('/api/v1/forum/attachments/legacy/1101-cancel', 'tok', 'notes.pdf'),
    ).resolves.toBeUndefined();
  });

  it('refuses a cookie-gated downloadUrl', async () => {
    await expect(
      openForumAttachmentFile('/forum/attachment/legacy/1101', 'tok', 'notes.pdf'),
    ).rejects.toMatchObject({ name: 'ApiError', status: 400 });
    expect(taskCalls).toHaveLength(0);
    expect(shareAsync).not.toHaveBeenCalled();
  });
});

describe('saveForumAttachmentImage', () => {
  it('saves the downloaded cache file to Photos', async () => {
    await saveForumAttachmentImage('/api/v1/forum/attachments/legacy/1043-save', 'tok', 'scan.jpg');
    expect(saveToLibrary).toHaveBeenCalledWith(
      expect.stringMatching(/^file:\/\/\/cache\/forum-attachments\/\d+\/scan\.jpg$/),
    );
    expect(writeAsString).not.toHaveBeenCalled();
    expect(shareAsync).not.toHaveBeenCalled();
  });
});

describe('openForumAttachmentImage', () => {
  it('refuses the cookie-gated path', async () => {
    await expect(openForumAttachmentImage('/forum/attachment/legacy/1002', 'tok')).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
    });
    expect(taskCalls).toHaveLength(0);
  });

  it('returns a cached file:// URI, not a data URI', async () => {
    const first = await openForumAttachmentImage('/api/v1/forum/attachments/legacy/1043-scan', 'tok');
    const second = await openForumAttachmentImage('/api/v1/forum/attachments/legacy/1043-scan', 'tok');
    expect(first).toMatch(/^file:/);
    expect(second).toBe(first);
    expect(taskCalls).toHaveLength(1);
  });
});
