import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';

const require = createRequire(import.meta.url);
const { stripReadMediaPermissions } = require('../../plugins/withAndroidAddOnlyPhotos.cjs') as {
  stripReadMediaPermissions: (manifest: {
    manifest: Record<string, { $?: { 'android:name'?: string } }[]>;
  }) => {
    manifest: Record<string, { $?: { 'android:name'?: string } }[]>;
  };
};

describe('stripReadMediaPermissions', () => {
  it('removes READ_MEDIA_* and keeps MediaStore write permissions', () => {
    const stripped = stripReadMediaPermissions({
      manifest: {
        'uses-permission': [
          { $: { 'android:name': 'android.permission.WRITE_EXTERNAL_STORAGE' } },
          { $: { 'android:name': 'android.permission.READ_MEDIA_IMAGES' } },
          { $: { 'android:name': 'android.permission.READ_MEDIA_VIDEO' } },
          { $: { 'android:name': 'android.permission.READ_MEDIA_AUDIO' } },
          { $: { 'android:name': 'android.permission.READ_MEDIA_VISUAL_USER_SELECTED' } },
        ],
      },
    });

    const names = stripped.manifest['uses-permission'].map((entry) => entry.$?.['android:name']);
    assert.deepEqual(names, ['android.permission.WRITE_EXTERNAL_STORAGE']);
  });
});

describe('add-only Photos plugin config', () => {
  it('registers savePhotosPermission only and not the image-picker string', () => {
    const appJson = JSON.parse(
      readFileSync(new URL('../../app.json', import.meta.url), 'utf8'),
    ) as { expo: { plugins: unknown[] } };
    const mediaLibrary = appJson.expo.plugins.find(
      (plugin) => Array.isArray(plugin) && plugin[0] === 'expo-media-library',
    ) as [string, { photosPermission: unknown; savePhotosPermission: string; granularPermissions: string[] }];
    const imagePicker = appJson.expo.plugins.find(
      (plugin) => Array.isArray(plugin) && plugin[0] === 'expo-image-picker',
    ) as [string, { photosPermission: string }];

    assert.equal(mediaLibrary[1].photosPermission, false);
    assert.equal(
      mediaLibrary[1].savePhotosPermission,
      'QueenZone adds a picture to your photo library when you tap Save, for example to keep a Queen gallery photo on your device.',
    );
    assert.deepEqual(mediaLibrary[1].granularPermissions, []);
    assert.notEqual(mediaLibrary[1].savePhotosPermission, imagePicker[1].photosPermission);

    const appConfig = readFileSync(new URL('../../app.config.ts', import.meta.url), 'utf8');
    assert.match(appConfig, /withAndroidAddOnlyPhotos\.cjs/);
  });
});
