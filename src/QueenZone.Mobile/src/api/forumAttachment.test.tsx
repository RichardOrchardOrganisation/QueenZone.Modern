import { Linking, Share } from 'react-native';
import * as Sharing from 'expo-sharing';
import { saveToLibraryAsync } from 'expo-media-library/legacy';
import { writeAsStringAsync } from 'expo-file-system/legacy';
import {
  cacheForumAttachment,
  fetchForumAttachment,
  isCookieGatedForumAttachmentPath,
  openForumAttachmentFile,
  openForumAttachmentImage,
  saveForumAttachmentImage,
} from './forumAttachment';

jest.mock('../config', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'http://qz.test' }),
  apiV1Url: (path: string) => `http://qz.test/api/v1${path.startsWith('/') ? path : `/${path}`}`,
}));

const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
const shareAsync = Sharing.shareAsync as jest.MockedFunction<typeof Sharing.shareAsync>;
const isAvailableAsync = Sharing.isAvailableAsync as jest.MockedFunction<
  typeof Sharing.isAvailableAsync
>;
const saveToLibrary = saveToLibraryAsync as jest.MockedFunction<typeof saveToLibraryAsync>;
const writeAsString = writeAsStringAsync as jest.MockedFunction<typeof writeAsStringAsync>;

beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
  jest.spyOn(Share, 'share').mockResolvedValue({ action: Share.sharedAction, activityType: undefined });
  isAvailableAsync.mockResolvedValue(true);
  shareAsync.mockResolvedValue(undefined);
  saveToLibrary.mockResolvedValue(undefined);
  writeAsString.mockResolvedValue(undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

function okResponse(contentType: string, bytes: number[], url: string): Response {
  return {
    ok: true,
    status: 200,
    url,
    headers: {
      get: (name: string) => (name.toLowerCase() === 'content-type' ? contentType : null),
    },
    arrayBuffer: async () => Uint8Array.from(bytes).buffer,
  } as unknown as Response;
}

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

describe('fetchForumAttachment', () => {
  it('refuses the cookie-gated path', async () => {
    await expect(fetchForumAttachment('/forum/attachment/legacy/1002', 'tok')).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('sends Bearer and returns the fetched bytes', async () => {
    fetchMock.mockResolvedValueOnce(okResponse('image/jpeg', [1, 2, 3], 'https://cdn2.queenzone.org/attachments/scan.jpg'));

    const result = await fetchForumAttachment('/api/v1/forum/attachments/legacy/1002', 'tok');
    expect(fetchMock).toHaveBeenCalledWith(
      'http://qz.test/api/v1/forum/attachments/legacy/1002',
      expect.objectContaining({
        method: 'GET',
        redirect: 'follow',
        headers: expect.objectContaining({ Authorization: 'Bearer tok' }),
      }),
    );
    expect(result.contentType).toBe('image/jpeg');
    expect(result.dataUri.startsWith('data:image/jpeg;base64,')).toBe(true);
  });

  it('maps 401 to a sign-in error', async () => {
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      url: 'http://qz.test/api/v1/forum/attachments/legacy/1002',
      headers: { get: () => null },
      arrayBuffer: async () => new ArrayBuffer(0),
    } as unknown as Response);
    await expect(fetchForumAttachment('/api/v1/forum/attachments/legacy/1002', 'tok')).rejects.toMatchObject({
      name: 'ApiError',
      status: 401,
      message: 'Sign in to continue.',
    });
  });
});

describe('openForumAttachmentFile', () => {
  it('shares a cached file:// URI after a CDN redirect and does not open the CDN', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse('application/pdf', [1], 'https://cdn2.queenzone.org/attachments/notes.pdf'),
    );

    await openForumAttachmentFile('/api/v1/forum/attachments/legacy/1101-share', 'tok', 'notes.pdf');
    expect(shareAsync).toHaveBeenCalledWith(
      'file:///cache/notes.pdf',
      expect.objectContaining({ mimeType: 'application/pdf', dialogTitle: 'notes.pdf' }),
    );
    expect(shareAsync.mock.calls[0]?.[0]).toMatch(/^file:/);
    expect(Linking.openURL).not.toHaveBeenCalled();
    expect(Share.share).not.toHaveBeenCalled();
  });

  it('shares a sound file as file:// and does not open the CDN', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse('audio/mpeg', [1], 'https://cdn2.queenzone.org/attachments/solo.mp3'),
    );

    await openForumAttachmentFile('/api/v1/forum/attachments/legacy/1201-share', 'tok', 'solo.mp3');
    expect(fetchMock).toHaveBeenCalledWith(
      'http://qz.test/api/v1/forum/attachments/legacy/1201-share',
      expect.objectContaining({
        method: 'GET',
        redirect: 'follow',
        headers: expect.objectContaining({ Authorization: 'Bearer tok' }),
      }),
    );
    expect(shareAsync).toHaveBeenCalledWith(
      'file:///cache/solo.mp3',
      expect.objectContaining({ mimeType: 'audio/mpeg' }),
    );
    expect(Linking.openURL).not.toHaveBeenCalled();
    expect(Share.share).not.toHaveBeenCalled();
  });

  it('shares streamed bytes from a file:// cache when the API does not redirect', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse(
        'text/plain',
        [9, 8],
        'http://qz.test/api/v1/forum/attachments/9001/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      ),
    );

    await openForumAttachmentFile(
      '/api/v1/forum/attachments/9001/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'tok',
      'notes.txt',
    );
    expect(Linking.openURL).not.toHaveBeenCalled();
    expect(Share.share).not.toHaveBeenCalled();
    expect(shareAsync).toHaveBeenCalledWith(
      'file:///cache/notes.txt',
      expect.objectContaining({ mimeType: 'text/plain', dialogTitle: 'notes.txt' }),
    );
  });

  it('skips the Files sheet when present is false', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse(
        'text/plain',
        [9, 8],
        'http://qz.test/api/v1/forum/attachments/9001/bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee',
      ),
    );

    await openForumAttachmentFile(
      '/api/v1/forum/attachments/9001/bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee',
      'tok',
      'attach.txt',
      { present: false },
    );
    expect(Linking.openURL).not.toHaveBeenCalled();
    expect(Share.share).not.toHaveBeenCalled();
    expect(shareAsync).not.toHaveBeenCalled();
    expect(writeAsString).toHaveBeenCalled();
  });

  it('does not treat a share cancel as an error', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse('application/pdf', [1], 'http://qz.test/api/v1/forum/attachments/legacy/1101-cancel'),
    );
    shareAsync.mockRejectedValueOnce(new Error('User cancelled sharing'));

    await expect(
      openForumAttachmentFile('/api/v1/forum/attachments/legacy/1101-cancel', 'tok', 'notes.pdf'),
    ).resolves.toBeUndefined();
  });

  it('refuses a cookie-gated downloadUrl', async () => {
    await expect(
      openForumAttachmentFile('/forum/attachment/legacy/1101', 'tok', 'notes.pdf'),
    ).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
    });
    expect(shareAsync).not.toHaveBeenCalled();
  });
});

describe('saveForumAttachmentImage', () => {
  it('writes the cached file and saves it to Photos', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse('image/jpeg', [1, 2, 3], 'https://cdn2.queenzone.org/attachments/scan.jpg'),
    );

    await saveForumAttachmentImage(
      '/api/v1/forum/attachments/legacy/1043-save',
      'tok',
      'anoto-setlist-scan.jpg',
    );

    expect(writeAsString).toHaveBeenCalledWith(
      'file:///cache/anoto-setlist-scan.jpg',
      expect.any(String),
      expect.objectContaining({ encoding: 'base64' }),
    );
    expect(saveToLibrary).toHaveBeenCalledWith('file:///cache/anoto-setlist-scan.jpg');
    expect(shareAsync).not.toHaveBeenCalled();
    expect(Share.share).not.toHaveBeenCalled();
    expect(Linking.openURL).not.toHaveBeenCalled();
  });
});

describe('cacheForumAttachment', () => {
  it('returns a file:// URI named with the real file name', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse('audio/mpeg', [4], 'https://cdn2.queenzone.org/attachments/solo.mp3'),
    );

    const cached = await cacheForumAttachment(
      '/api/v1/forum/attachments/legacy/1201-cache',
      'tok',
      'brighton-rock-solo.mp3',
    );
    expect(cached.fileUri).toBe('file:///cache/brighton-rock-solo.mp3');
    expect(cached.contentType).toBe('audio/mpeg');
  });
});

describe('openForumAttachmentImage', () => {
  it('refuses the cookie-gated path', async () => {
    await expect(openForumAttachmentImage('/forum/attachment/legacy/1002', 'tok')).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('follows the legacy redirect and returns a cached data URI', async () => {
    fetchMock.mockResolvedValue(
      okResponse('image/jpeg', [1, 2, 3], 'https://cdn2.queenzone.org/attachments/scan.jpg'),
    );

    const first = await openForumAttachmentImage('/api/v1/forum/attachments/legacy/1043-scan', 'tok');
    const second = await openForumAttachmentImage('/api/v1/forum/attachments/legacy/1043-scan', 'tok');

    expect(first).toMatch(/^data:image\/jpeg;base64,/);
    expect(second).toBe(first);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://qz.test/api/v1/forum/attachments/legacy/1043-scan',
      expect.objectContaining({ redirect: 'follow' }),
    );
  });

  it('refuses a cookie-gated final URL after follow', async () => {
    fetchMock.mockResolvedValueOnce(
      okResponse('image/jpeg', [1], 'http://qz.test/forum/attachment/legacy/1002'),
    );

    await expect(
      openForumAttachmentImage('/api/v1/forum/attachments/legacy/1043-cookie-final', 'tok'),
    ).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
    });
  });
});
