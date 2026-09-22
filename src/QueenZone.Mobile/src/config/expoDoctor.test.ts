import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

type PackageConfig = {
  expo?: {
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
