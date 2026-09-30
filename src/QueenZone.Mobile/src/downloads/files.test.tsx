import { createMemoryStorage } from '../cache/storage';
import { getDownloadFileHost, setDownloadFileHostForTests } from './files';
import { getCompletedDownload, setDownloadManifestStorageForTests, upsertCompletedDownload } from './manifest';
import { purgeAllDownloads, resetDownloadManagerForTests } from './manager';

const mockFiles = new Set<string>();
let mockDeleteFails = false;
let mockListFails = false;
const uri = 'file:///documents/fan-performances/187.mp3';

jest.mock('expo-file-system', () => {
  class File {
    uri: string;
    constructor(value: string) { this.uri = value; }
    get exists() { return mockFiles.has(this.uri); }
    delete() {
      if (mockDeleteFails) throw new Error('native deletion failed with private path');
      mockFiles.delete(this.uri);
    }
  }
  class Directory {
    exists = true;
    list() {
      if (mockListFails) throw new Error('native listing failed with private path');
      return [...mockFiles].map((value) => new File(value));
    }
  }
  return {
    File, Directory,
    Paths: { document: { uri: 'file:///documents' }, availableDiskSpace: 1_000_000 },
  };
});

beforeEach(() => {
  mockFiles.clear();
  mockDeleteFails = false;
  mockListFails = false;
  setDownloadFileHostForTests(null);
  setDownloadManifestStorageForTests(createMemoryStorage());
  resetDownloadManagerForTests();
});

afterEach(() => {
  setDownloadFileHostForTests(null);
  setDownloadManifestStorageForTests(null);
});

async function seedManifest() {
  mockFiles.add(uri);
  await upsertCompletedDownload({
    performanceId: '187', localUri: uri, title: 'Recording', performedBy: 'Member',
    byteSize: 4, sourceRevision: null, completedAt: '2026-09-30T00:00:00.000Z', memberId: 'member-1',
  });
}

describe('native download cleanup failures', () => {
  it('keeps ordinary deletion best effort but reports strict deletion failure', () => {
    mockFiles.add(uri);
    mockDeleteFails = true;
    const host = getDownloadFileHost();
    expect(() => host.deleteIfExists(uri)).not.toThrow();
    expect(() => host.deleteIfExists(uri, true)).toThrow('Downloaded file cleanup failed');
    expect(mockFiles.has(uri)).toBe(true);
  });

  it('keeps ordinary listing best effort but reports strict listing failure', () => {
    mockListFails = true;
    const host = getDownloadFileHost();
    expect(host.listAllUris()).toEqual([]);
    expect(() => host.listAllUris(true)).toThrow('Downloaded file listing failed');
  });

  it('retains the manifest when native deletion fails and retries successfully', async () => {
    await seedManifest();
    mockDeleteFails = true;
    await expect(purgeAllDownloads('member-1')).rejects.toThrow('Downloaded file cleanup failed');
    expect(mockFiles.has(uri)).toBe(true);
    expect(await getCompletedDownload('member-1', '187')).not.toBeNull();
    mockDeleteFails = false;
    await expect(purgeAllDownloads('member-1')).resolves.toBeUndefined();
    expect(mockFiles.has(uri)).toBe(false);
    expect(await getCompletedDownload('member-1', '187')).toBeNull();
  });

  it('retains the manifest when native listing fails and retries successfully', async () => {
    await seedManifest();
    mockListFails = true;
    await expect(purgeAllDownloads('member-1')).rejects.toThrow('Downloaded file listing failed');
    expect(await getCompletedDownload('member-1', '187')).not.toBeNull();
    mockListFails = false;
    await expect(purgeAllDownloads('member-1')).resolves.toBeUndefined();
    expect(mockFiles.has(uri)).toBe(false);
    expect(await getCompletedDownload('member-1', '187')).toBeNull();
  });
});
