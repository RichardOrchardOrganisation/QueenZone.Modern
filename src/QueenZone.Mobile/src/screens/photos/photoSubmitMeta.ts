import type { PhotoUploadFile } from '../../api/photoSubmissionForm';

export const photoTitleMaxLength = 200;
export const photoDescriptionMaxLength = 1000;
export const photoCategoryMaxLength = 100;
export const photoMinYear = 1900;
export const photoMaxYear = 2100;
export const photoMaxUploadBytes = 20 * 1024 * 1024;

export const allowedPhotoMimeTypes = new Set(['image/jpeg', 'image/png', 'image/webp', 'image/tiff']);

export const photoSubmitCopy = {
  eyebrow: 'Community',
  title: 'Submit a photo',
  intro:
    'Submissions are reviewed by editors before they appear in the public gallery. One photo per submission — you can submit again for additional photos.',
  help: 'JPEG, PNG, WebP, or TIFF. Max 20 MB. The original is kept for the archive; a web copy and thumbnail are generated automatically.',
  confirmationTitle: 'Photo submitted',
  confirmationMessage: 'Your photo is under review.',
  submitAction: 'Submit for review',
  anotherAction: 'Submit another photo',
  categoryLabel: 'Category',
  categoryRequired: 'Select a category.',
  categoriesLoadError: 'Could not load photo categories.',
} as const;

/** Keep originals for the archive; the screen adds Compatible representation to avoid HEIC. */
export const archiveImagePickerOptions = {
  mediaTypes: ['images'] as 'images'[],
  quality: 1 as const,
  allowsEditing: false as const,
};

export type PickerImageAsset = {
  uri: string;
  fileName?: string | null;
  mimeType?: string | null;
  fileSize?: number | null;
};

export type PhotoSubmitFields = {
  title: string;
  description: string;
  suggestedCategory: string;
  approximateYear: string;
  approximateDate: string;
  photo: PhotoUploadFile | null;
  fileSize?: number | null;
};

export function extensionFromName(value: string): string {
  const trimmed = value.trim();
  const query = trimmed.indexOf('?');
  const path = query >= 0 ? trimmed.slice(0, query) : trimmed;
  const slash = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  const name = slash >= 0 ? path.slice(slash + 1) : path;
  const dot = name.lastIndexOf('.');
  return dot >= 0 ? name.slice(dot + 1).toLowerCase() : '';
}

export function mimeFromExtension(extension: string): string | null {
  switch (extension) {
    case 'jpg':
    case 'jpeg':
      return 'image/jpeg';
    case 'png':
      return 'image/png';
    case 'webp':
      return 'image/webp';
    case 'tif':
    case 'tiff':
      return 'image/tiff';
    default:
      return null;
  }
}

export function normalizePhotoMimeType(
  mimeType: string | null | undefined,
  fileName: string,
): string | null {
  const mime = (mimeType ?? '').trim().toLowerCase();
  if (mime === 'image/jpg') {
    return 'image/jpeg';
  }

  if (allowedPhotoMimeTypes.has(mime)) {
    return mime;
  }

  return mimeFromExtension(extensionFromName(fileName));
}

export function defaultPhotoFileName(mimeType: string, uri: string): string {
  const fromUri = extensionFromName(uri);
  if (fromUri && mimeFromExtension(fromUri)) {
    const slash = Math.max(uri.lastIndexOf('/'), uri.lastIndexOf('\\'));
    const name = slash >= 0 ? uri.slice(slash + 1) : uri;
    const query = name.indexOf('?');
    return query >= 0 ? name.slice(0, query) : name;
  }

  const extensions = new Map([
    ['image/png', 'png'],
    ['image/webp', 'webp'],
    ['image/tiff', 'tiff'],
  ]);
  const extension = extensions.get(mimeType) ?? 'jpg';
  return `photo.${extension}`;
}

export function photoFromPickerAsset(
  asset: PickerImageAsset,
): { photo: PhotoUploadFile; fileSize: number | null } | { error: string } {
  const uri = asset.uri?.trim() ?? '';
  if (!uri) {
    return { error: 'Choose a photo to upload.' };
  }

  const fileName = (asset.fileName?.trim() || defaultPhotoFileName(asset.mimeType ?? '', uri)).trim();
  const type = normalizePhotoMimeType(asset.mimeType, fileName) ?? normalizePhotoMimeType(asset.mimeType, uri);
  if (!type) {
    return { error: 'Photo must be a JPEG, PNG, WebP, or TIFF image.' };
  }

  const fileSize = typeof asset.fileSize === 'number' && Number.isFinite(asset.fileSize) ? asset.fileSize : null;
  if (fileSize != null && fileSize > photoMaxUploadBytes) {
    return { error: 'Photo must be 20 MB or smaller.' };
  }

  return {
    photo: {
      uri,
      name: fileName.includes('.') ? fileName : defaultPhotoFileName(type, uri),
      type,
    },
    fileSize,
  };
}

export function parseApproximateYear(value: string): number | null | { error: string } {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  if (!/^-?\d+$/.test(trimmed)) {
    return { error: 'Year must be between 1900 and 2100.' };
  }

  const year = Number.parseInt(trimmed, 10);
  if (year < photoMinYear || year > photoMaxYear) {
    return { error: 'Year must be between 1900 and 2100.' };
  }

  return year;
}

export function parseApproximateDate(value: string): string | null | { error: string } {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (!match) {
    return { error: 'Approximate date must be YYYY-MM-DD.' };
  }

  const year = Number.parseInt(match[1] ?? '', 10);
  const month = Number.parseInt(match[2] ?? '', 10);
  const day = Number.parseInt(match[3] ?? '', 10);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year
    || date.getUTCMonth() !== month - 1
    || date.getUTCDate() !== day
  ) {
    return { error: 'Approximate date must be a real calendar date.' };
  }

  return trimmed;
}

export function validatePhotoSubmit(input: PhotoSubmitFields): string | null {
  const title = input.title.trim();
  if (!title) {
    return 'Title is required.';
  }

  if (title.length > photoTitleMaxLength) {
    return 'Title must be 200 characters or fewer.';
  }

  if (input.description.trim().length > photoDescriptionMaxLength) {
    return 'Description must be 1000 characters or fewer.';
  }

  const category = input.suggestedCategory.trim();
  if (!category) {
    return photoSubmitCopy.categoryRequired;
  }

  if (category.length > photoCategoryMaxLength) {
    return 'Suggested category must be 100 characters or fewer.';
  }

  const year = parseApproximateYear(input.approximateYear);
  if (year && typeof year === 'object') {
    return year.error;
  }

  const date = parseApproximateDate(input.approximateDate);
  if (date && typeof date === 'object') {
    return date.error;
  }

  if (!input.photo) {
    return 'Choose a photo to upload.';
  }

  if (input.fileSize != null && input.fileSize > photoMaxUploadBytes) {
    return 'Photo must be 20 MB or smaller.';
  }

  if (!allowedPhotoMimeTypes.has(input.photo.type)) {
    return 'Photo must be a JPEG, PNG, WebP, or TIFF image.';
  }

  return null;
}

export function formatSubmittedAt(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  return date.toISOString().replace('T', ' ').replace(/\.\d+Z$/, 'Z');
}
