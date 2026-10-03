import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import type { PhotoDetail } from '../../api/types';
import { photoViewerActions, photoViewerDisplay } from './photoViewerDisplay.ts';

const photo: PhotoDetail = {
  picId: 101, catId: 1, categoryName: 'Brian May', categorySlug: 'brian-may', title: 'Live Aid',
  imageUrl: 'https://cdn.queenzone.org/brian-may/img-101.jpg', thumbnailUrl: 'https://cdn.queenzone.org/brian-may/img-101-t.jpg',
  thumbWidth: 200, thumbHeight: 150, pictureWidth: 1600, pictureHeight: 900, pictureDimensionsLabel: '1600 x 900',
  year: 1985, dateTime: '1985-07-13T00:00:00.000Z', submittedByDisplayName: 'QueenFan',
  detailPath: '/photography/brian-may/101', categoryPath: '/photography/brian-may', index: 0, count: 3,
  previous: { picId: 100, detailPath: '/photography/brian-may/100', imageUrl: 'https://cdn.queenzone.org/brian-may/img-100.jpg', pictureWidth: 1200, pictureHeight: 800 },
  next: { picId: 102, detailPath: '/photography/brian-may/102', imageUrl: 'https://cdn.queenzone.org/brian-may/img-102.jpg' },
};

describe('photo viewer display selection', () => {
  it('uses the loaded image, dimensions and both prefetch neighbors for the current route', () => {
    assert.deepEqual(photoViewerDisplay(photo, 101), {
      previousPicId: 100, nextPicId: 102, image: { uri: photo.imageUrl },
      neighbourUris: [photo.previous!.imageUrl, photo.next!.imageUrl],
      recyclingKey: 'photo-full-101', imageWidth: 1600, imageHeight: 900, resetKey: 101, pending: false,
    });
  });
  it('immediately displays a known previous or next image while its detail request is pending', () => {
    const previous = photoViewerDisplay(photo, 100);
    assert.deepEqual(previous.image, { uri: photo.previous!.imageUrl });
    assert.equal(previous.imageWidth, 1200); assert.equal(previous.imageHeight, 800);
    assert.equal(previous.resetKey, 100); assert.equal(previous.recyclingKey, 'photo-full-100');
    assert.equal(previous.pending, false); assert.deepEqual(previous.neighbourUris, []);
    const next = photoViewerDisplay(photo, 102);
    assert.deepEqual(next.image, { uri: photo.next!.imageUrl });
    assert.equal(next.imageWidth, 0); assert.equal(next.imageHeight, 0);
    assert.equal(next.resetKey, 102); assert.equal(next.pending, false);
  });
  it('keeps the current image pending for an unknown or unavailable neighboring image', () => {
    for (const [detail, route] of [[photo, 999], [{ ...photo, previous: { picId: 100, detailPath: '/100', imageUrl: '/unsafe.png' } }, 100]] as const) {
      const view = photoViewerDisplay(detail, route);
      assert.deepEqual(view.image, { uri: photo.imageUrl });
      assert.equal(view.pending, true); assert.equal(view.resetKey, 101);
    }
  });
  it('handles missing data, images and neighbor metadata without inventing prefetch URLs', () => {
    const absent = photoViewerDisplay(null, 101);
    assert.equal(absent.image, null); assert.equal(absent.previousPicId, null); assert.equal(absent.nextPicId, null);
    assert.equal(absent.imageWidth, 0); assert.equal(absent.imageHeight, 0); assert.equal(absent.resetKey, undefined);
    const end = photoViewerDisplay({ ...photo, imageUrl: '/unsafe', previous: null, next: null }, 101);
    assert.equal(end.image, null); assert.deepEqual(end.neighbourUris, [undefined, undefined]);
    const unknown = photoViewerDisplay({ ...photo, previous: null, next: null }, 999);
    assert.equal(unknown.pending, true);
  });
});

describe('photo action indicators', () => {
  for (const platform of ['ios', 'android', 'web'])
  for (const photosCooldown of [false, true])
  for (const wallpaperCooldown of [false, true])
  for (const saveBusy of [false, true])
  for (const wallpaperBusy of [false, true]) {
    it(`${platform} photos=${photosCooldown} wallpaper=${wallpaperCooldown} saving=${saveBusy} setting=${wallpaperBusy}`, () => {
      assert.deepEqual(photoViewerActions(platform, photosCooldown, wallpaperCooldown, saveBusy, wallpaperBusy), {
        busy: saveBusy || wallpaperBusy,
        saveComplete: photosCooldown && !saveBusy,
        wallpaperComplete: (platform === 'ios' ? photosCooldown : wallpaperCooldown) && !wallpaperBusy,
      });
    });
  }
});
