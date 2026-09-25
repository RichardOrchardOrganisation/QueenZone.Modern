import { Directory, File, FileMode, Paths } from 'expo-file-system';
import { trimTrailingChar } from '../text/trimRuns';
import type { DownloadAudioExtension } from './audioBytes';
import { DOWNLOAD_EMPTY_PART_MESSAGE, DOWNLOAD_PART_MISSING_MESSAGE } from './messages';
import { DOWNLOAD_DIRECTORY_NAME } from './types';

export type DownloadProbe = {
  sourceRevision: string | null;
  byteSize: number | null;
};

export type DownloadFileHost = {
  documentDirectoryUri(): string;
  availableBytes(): number;
  completedUri(performanceId: string, extension: DownloadAudioExtension | null): string;
  partUri(performanceId: string): string;
  exists(uri: string): boolean;
  size(uri: string): number;
  deleteIfExists(uri: string): void;
  listPartUris(): string[];
  listAllUris(): string[];
  promote(partUri: string, completedUri: string): void | Promise<void>;
  writeBytes(uri: string, bytes: Uint8Array): void;
  readPrefix(uri: string, maxBytes: number): Promise<Uint8Array | null>;
  download(input: {
    url: string;
    destUri: string;
    headers: Record<string, string>;
    onProgress?: (written: number, total: number) => void;
    signal?: AbortSignal;
  }): Promise<{ uri?: string } | void>;
};

function joinUri(root: string, name: string): string {
  return `${trimTrailingChar(root, '/')}/${name}`;
}

export function opaqueFileName(
  performanceId: string,
  extension: DownloadAudioExtension | 'part' | null,
): string {
  const id = performanceId.replace(/[^A-Za-z0-9_-]/g, '');
  return extension ? `${id}.${extension}` : id;
}

function createNativeHost(): DownloadFileHost {
  function audioDir(): Directory {
    const dir = new Directory(Paths.document, DOWNLOAD_DIRECTORY_NAME);
    if (!dir.exists) {
      dir.create({ intermediates: true, idempotent: true });
    }
    return dir;
  }

  function fileFor(uri: string): File {
    return new File(uri);
  }

  return {
    documentDirectoryUri() {
      return joinUri(Paths.document.uri, DOWNLOAD_DIRECTORY_NAME);
    },
    availableBytes() {
      const space = Paths.availableDiskSpace;
      return typeof space === 'number' && Number.isFinite(space) ? space : Number.POSITIVE_INFINITY;
    },
    completedUri(performanceId, extension) {
      return new File(audioDir(), opaqueFileName(performanceId, extension)).uri;
    },
    partUri(performanceId) {
      return new File(audioDir(), opaqueFileName(performanceId, 'part')).uri;
    },
    exists(uri) {
      try {
        return fileFor(uri).exists;
      } catch {
        return false;
      }
    },
    size(uri) {
      try {
        const file = fileFor(uri);
        return file.exists ? file.size : 0;
      } catch {
        return 0;
      }
    },
    deleteIfExists(uri) {
      try {
        const file = fileFor(uri);
        if (file.exists) {
          file.delete();
        }
      } catch {
        // Best-effort cleanup.
      }
    },
    listAllUris() {
      try {
        return audioDir()
          .list()
          .filter((entry): entry is File => entry instanceof File)
          .map((entry) => entry.uri);
      } catch {
        return [];
      }
    },
    listPartUris() {
      try {
        return audioDir()
          .list()
          .filter((entry): entry is File => entry instanceof File && entry.uri.endsWith('.part'))
          .map((entry) => entry.uri);
      } catch {
        return [];
      }
    },
    promote(partUri, completedUri) {
      const part = fileFor(partUri);
      if (!part.exists) {
        throw new Error(DOWNLOAD_PART_MISSING_MESSAGE);
      }
      if (!part.size || part.size <= 0) {
        throw new Error(DOWNLOAD_EMPTY_PART_MESSAGE);
      }
      const completed = fileFor(completedUri);
      if (completed.exists) {
        completed.delete();
      }
      return part.move(completed);
    },
    writeBytes(uri, bytes) {
      const file = fileFor(uri);
      if (!file.exists) {
        file.create({ intermediates: true, overwrite: true });
      }
      file.write(bytes);
    },
    async readPrefix(uri, maxBytes) {
      try {
        const file = fileFor(uri);
        if (!file.exists) {
          return null;
        }
        const handle = file.open(FileMode.ReadOnly);
        try {
          return handle.readBytes(maxBytes);
        } finally {
          handle.close();
        }
      } catch {
        return null;
      }
    },
    async download({ url, destUri, headers, onProgress, signal }) {
      audioDir();
      const dest = fileFor(destUri);
      if (dest.exists) {
        dest.delete();
      }
      // The task API reported successful full GETs without leaving a usable
      // file on both platforms. The one-shot API owns transfer + destination
      // materialisation as one native operation and still supports progress
      // and cancellation.
      const file = await File.downloadFileAsync(url, dest, {
        headers,
        idempotent: true,
        signal,
        onProgress: onProgress
          ? ({ bytesWritten, totalBytes }) => {
              onProgress(bytesWritten, totalBytes);
            }
          : undefined,
      });
      if (!file) {
        throw new Error('Download did not complete.');
      }
      const returnedUri = typeof file.uri === 'string' ? file.uri.trim() : '';
      return { uri: returnedUri || dest.uri };
    },
  };
}

let host: DownloadFileHost = createNativeHost();

export function getDownloadFileHost(): DownloadFileHost {
  return host;
}

export function setDownloadFileHostForTests(next: DownloadFileHost | null): void {
  host = next ?? createNativeHost();
}

export function createMemoryDownloadHost(
  options: {
    availableBytes?: number;
    downloadImpl?: DownloadFileHost['download'];
  } = {},
): DownloadFileHost & { files: Map<string, Uint8Array> } {
  const files = new Map<string, Uint8Array>();
  const root = 'file:///documents/fan-performances';
  const downloadImpl =
    options.downloadImpl ??
    (async ({ destUri }) => {
      files.set(destUri, new Uint8Array([1, 2, 3, 4]));
      return { uri: destUri };
    });

  return {
    files,
    documentDirectoryUri: () => root,
    availableBytes: () => options.availableBytes ?? 64 * 1024 * 1024,
    completedUri: (performanceId, extension) => joinUri(root, opaqueFileName(performanceId, extension)),
    partUri: (performanceId) => joinUri(root, opaqueFileName(performanceId, 'part')),
    exists: (uri) => files.has(uri),
    size: (uri) => files.get(uri)?.byteLength ?? 0,
    deleteIfExists: (uri) => {
      files.delete(uri);
    },
    listPartUris: () => [...files.keys()].filter((uri) => uri.endsWith('.part')),
    listAllUris: () => [...files.keys()],
    promote: (partUri, completedUri) => {
      const bytes = files.get(partUri);
      if (!bytes) {
        throw new Error(DOWNLOAD_PART_MISSING_MESSAGE);
      }
      if (bytes.byteLength <= 0) {
        throw new Error(DOWNLOAD_EMPTY_PART_MESSAGE);
      }
      files.delete(partUri);
      files.set(completedUri, bytes);
    },
    writeBytes: (uri, bytes) => {
      files.set(uri, bytes);
    },
    readPrefix: async (uri, maxBytes) => {
      const bytes = files.get(uri);
      if (!bytes) {
        return null;
      }
      return bytes.subarray(0, Math.min(maxBytes, bytes.length));
    },
    download: downloadImpl,
  };
}
