import type { PhotoDetail } from '../../api/types';
import { photoCdnSource } from './photoGalleryMeta.ts';

function requestedNeighbor(photo: PhotoDetail | null, picId: number) {
  if (photo == null || photo.picId === picId) return null;
  if (photo.previous?.picId === picId) return photo.previous;
  if (photo.next?.picId === picId) return photo.next;
  return null;
}

function neighborPrefetchUris(photo: PhotoDetail | null, picId: number) {
  if (photo == null || photo.picId !== picId) return [];
  return [photoCdnSource(photo.previous?.imageUrl)?.uri, photoCdnSource(photo.next?.imageUrl)?.uri];
}

export function photoViewerDisplay(photo: PhotoDetail | null, picId: number) {
  const neighbor = requestedNeighbor(photo, picId);
  const neighborSource = photoCdnSource(neighbor?.imageUrl);
  const displayingNeighbor = neighborSource != null;
  const currentSource = photoCdnSource(photo?.imageUrl);
  const image = displayingNeighbor ? neighborSource : currentSource;
  return {
    previousPicId: photo?.previous?.picId ?? null,
    nextPicId: photo?.next?.picId ?? null,
    image,
    neighbourUris: neighborPrefetchUris(photo, picId),
    recyclingKey: `photo-full-${displayingNeighbor ? picId : photo?.picId}`,
    imageWidth: displayingNeighbor ? neighbor?.pictureWidth ?? 0 : photo?.pictureWidth ?? 0,
    imageHeight: displayingNeighbor ? neighbor?.pictureHeight ?? 0 : photo?.pictureHeight ?? 0,
    resetKey: displayingNeighbor ? picId : photo?.picId,
    pending: photo?.picId !== picId && !displayingNeighbor,
  };
}

export function photoViewerActions(platform: string, photosCooldown: boolean, wallpaperCooldown: boolean, saveBusy: boolean, wallpaperBusy: boolean) {
  const wallpaperOnCooldown = platform === 'ios' ? photosCooldown : wallpaperCooldown;
  return {
    busy: saveBusy || wallpaperBusy,
    saveComplete: photosCooldown && !saveBusy,
    wallpaperComplete: wallpaperOnCooldown && !wallpaperBusy,
  };
}
