import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

type PackageConfig = {
  expo?: {
    install?: {
      exclude?: string[];
    };
    doctor?: {
      reactNativeDirectoryCheck?: {
        enabled?: boolean;
        exclude?: string[];
        listUnknownPackages?: boolean;
      };
    };
  };
};

const packageConfig = JSON.parse(
  readFileSync(new URL('../../package.json', import.meta.url), 'utf8'),
) as PackageConfig;

describe('Expo Doctor React Native Directory policy', () => {
  it('keeps the check enabled with only documented package exceptions', () => {
    const directoryCheck = packageConfig.expo?.doctor?.reactNativeDirectoryCheck;

    assert.notEqual(directoryCheck?.enabled, false);
    assert.notEqual(directoryCheck?.listUnknownPackages, false);
    assert.deepEqual(directoryCheck?.exclude, ['queenzone-wallpaper']);
  });
});

describe('Expo SDK 58 native pins (#1782)', () => {
  const bundled = JSON.parse(readFileSync(new URL('../../node_modules/expo/bundledNativeModules.json', import.meta.url), 'utf8')) as Record<string, string>;
  for (const name of ['@sentry/react-native', 'react-native-webview']) {
    it(`validates ${name} against the SDK bundle without an exclusion`, () => {
      assert.ok(!packageConfig.expo?.install?.exclude?.includes(name));
      const installed = JSON.parse(readFileSync(new URL(`../../node_modules/${name}/package.json`, import.meta.url), 'utf8')) as { version: string };
      assert.equal(installed.version, bundled[name].replace(/^~/, ''));
    });
  }
});
