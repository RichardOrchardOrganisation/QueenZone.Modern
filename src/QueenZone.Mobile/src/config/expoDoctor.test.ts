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

describe('Expo install exclude for Sentry 8 on SDK 57 (#1782)', () => {
  it('keeps @sentry/react-native out of expo install --check until SDK 58', () => {
    assert.deepEqual(packageConfig.expo?.install?.exclude, ['@sentry/react-native']);
  });
});
