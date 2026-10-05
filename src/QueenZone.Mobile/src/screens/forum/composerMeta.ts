import type { UploadFilePart } from '../../api/uploadFile';

export const subjectMinLength = 5;
export const subjectMaxLength = 200;

export type ComposerMode = 'reply' | 'newTopic';
export type ComposerAttachment = UploadFilePart;

/** Same as PhotoSubmitScreen: originals only, Compatible conversion is added at the call site. */
export const forumImagePickerOptions = {
  mediaTypes: ['images'] as 'images'[],
  quality: 1 as const,
  allowsEditing: false as const,
};

export const composerAttachCopy = {
  photos: 'Photos',
  files: 'Files',
  remove: 'Remove attachment',
  photosPermission: 'Photo library permission is required to choose a photo.',
  photosUnavailable: 'Could not open the photo library.',
  filesUnavailable: 'Could not open the file picker.',
  missingFile: 'Could not read the selected file. Try choosing it again.',
  oneFile: 'One file per post.',
} as const;

export type ComposerPickerAsset = {
  uri?: string | null;
  name?: string | null;
  fileName?: string | null;
  mimeType?: string | null;
};

export type ComposerParams = {
  threadId?: number;
  threadTitle?: string;
  categoryId?: number;
  categoryName?: string;
  isLocked?: boolean;
};

export function composerMode(params: ComposerParams | undefined): ComposerMode {
  return params?.threadId != null ? 'reply' : 'newTopic';
}

export function validateComposer(input: {
  mode: ComposerMode;
  title: string;
  body: string;
  categoryId?: number;
  isLocked?: boolean;
}): string | null {
  if (input.mode === 'reply' && input.isLocked) {
    return 'This topic is locked.';
  }

  if (!input.body.trim()) {
    return 'Write a post before publishing.';
  }

  if (input.mode === 'newTopic') {
    if (input.categoryId == null || input.categoryId <= 0) {
      return 'Choose a board for this topic.';
    }

    const title = input.title.trim();
    if (title.length < subjectMinLength || title.length > subjectMaxLength) {
      return 'Title must be between 5 and 200 characters.';
    }
  }

  return null;
}

export function composerCopy(mode: ComposerMode): { title: string; action: string } {
  return mode === 'reply'
    ? { title: 'Reply', action: 'Post reply' }
    : { title: 'New topic', action: 'Post topic' };
}

export function fileNameFromUri(uri: string): string {
  const trimmed = uri.trim();
  const query = trimmed.indexOf('?');
  const path = query >= 0 ? trimmed.slice(0, query) : trimmed;
  const slash = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  const name = slash >= 0 ? path.slice(slash + 1) : path;
  try {
    return decodeURIComponent(name);
  } catch {
    return name;
  }
}

export function guessForumAttachmentType(fileName: string): string {
  const name = fileNameFromUri(fileName);
  const dot = name.lastIndexOf('.');
  const ext = dot >= 0 ? name.slice(dot + 1).toLowerCase() : '';
  switch (ext) {
    case 'jpg':
    case 'jpeg':
      return 'image/jpeg';
    case 'png':
      return 'image/png';
    case 'gif':
      return 'image/gif';
    case 'webp':
      return 'image/webp';
    case 'pdf':
      return 'application/pdf';
    case 'zip':
      return 'application/zip';
    case 'mp3':
      return 'audio/mpeg';
    case 'flac':
      return 'audio/flac';
    case 'txt':
      return 'text/plain';
    case 'doc':
      return 'application/msword';
    case 'docx':
      return 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
    case 'xls':
      return 'application/vnd.ms-excel';
    case 'xlsx':
      return 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    case 'ppt':
      return 'application/vnd.ms-powerpoint';
    case 'pptx':
      return 'application/vnd.openxmlformats-officedocument.presentationml.presentation';
    default:
      return 'application/octet-stream';
  }
}

export function attachmentFromPickerAsset(
  asset: ComposerPickerAsset,
): ComposerAttachment | { error: string } {
  const uri = asset.uri?.trim() ?? '';
  if (!uri) {
    return { error: composerAttachCopy.missingFile };
  }

  const name = (asset.fileName?.trim() || asset.name?.trim() || fileNameFromUri(uri) || 'attachment').trim();
  const mime = (asset.mimeType ?? '').trim().toLowerCase();
  const normalizedMime = mime === 'image/jpg' ? 'image/jpeg' : mime;
  const type = mime && mime !== 'application/octet-stream'
    ? normalizedMime
    : guessForumAttachmentType(name);

  return { uri, name, type };
}
