import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  claimsWallpaperSet,
  wallpaperCopy,
  wallpaperFailureMessage,
  wallpaperTargets,
} from './wallpaperMeta.ts';

describe('wallpaper copy (#1409 Option A)', () => {
  it('never lets the iOS success line claim wallpaper was set', () => {
    assert.equal(claimsWallpaperSet(wallpaperCopy.iosSaved), false);
    assert.match(wallpaperCopy.iosSaved, /Saved to Photos/);
    assert.match(wallpaperCopy.iosSaved, /Settings → Wallpaper/);
    assert.doesNotMatch(wallpaperCopy.iosSaved, /Wallpaper set/);
  });

  it('keeps the iOS already-saved cooldown line honest', () => {
    assert.equal(claimsWallpaperSet(wallpaperCopy.iosAlreadySaved), false);
    assert.match(wallpaperCopy.iosAlreadySaved, /Already saved/);
    assert.match(wallpaperCopy.iosAlreadySaved, /Settings → Wallpaper/);
    assert.doesNotMatch(wallpaperCopy.iosAlreadySaved, /Wallpaper set/);
  });

  it('reserves Wallpaper set for the Android success path only', () => {
    assert.equal(claimsWallpaperSet(wallpaperCopy.androidSet), true);
    assert.equal(wallpaperCopy.androidSet, 'Wallpaper set.');
  });

  it('names the Android sheet targets Home screen / Lock screen / Both', () => {
    assert.deepEqual(
      wallpaperTargets.map((option) => option.label),
      ['Home screen', 'Lock screen', 'Both'],
    );
    assert.deepEqual(
      wallpaperTargets.map((option) => option.target),
      ['home', 'lock', 'both'],
    );
  });

  it('maps native failures to the chosen target', () => {
    assert.equal(wallpaperFailureMessage('home', 'target-failed'), wallpaperCopy.homeFailed);
    assert.equal(wallpaperFailureMessage('lock', 'target-failed'), wallpaperCopy.lockFailed);
    assert.equal(wallpaperFailureMessage('both'), wallpaperCopy.bothFailed);
    assert.equal(wallpaperFailureMessage('lock', 'unsupported'), wallpaperCopy.unsupported);
  });
});
