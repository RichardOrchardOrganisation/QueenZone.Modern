/**
 * expo-media-library always adds READ_MEDIA_VISUAL_USER_SELECTED even when
 * granularPermissions is []. Forum/gallery save is add-only MediaStore — strip
 * every READ_MEDIA_* so install does not request photo/video/audio read.
 */
const { createRunOncePlugin, withAndroidManifest } = require('expo/config-plugins');

const TAG = 'queenzone-add-only-photos';
const READ_MEDIA_PREFIX = 'android.permission.READ_MEDIA_';

function permissionName(entry) {
  return entry?.$?.['android:name'] ?? '';
}

function stripReadMediaEntries(list) {
  if (!Array.isArray(list)) {
    return list;
  }
  return list.filter((entry) => !permissionName(entry).startsWith(READ_MEDIA_PREFIX));
}

function stripReadMediaPermissions(manifest) {
  const root = manifest?.manifest;
  if (!root || typeof root !== 'object') {
    return manifest;
  }
  if (root['uses-permission']) {
    root['uses-permission'] = stripReadMediaEntries(root['uses-permission']);
  }
  if (root['uses-permission-sdk-23']) {
    root['uses-permission-sdk-23'] = stripReadMediaEntries(root['uses-permission-sdk-23']);
  }
  return manifest;
}

function withAndroidAddOnlyPhotos(config) {
  return withAndroidManifest(config, (mod) => {
    mod.modResults = stripReadMediaPermissions(mod.modResults);
    return mod;
  });
}

const plugin = createRunOncePlugin(withAndroidAddOnlyPhotos, TAG);

module.exports = plugin;
module.exports.stripReadMediaPermissions = stripReadMediaPermissions;
