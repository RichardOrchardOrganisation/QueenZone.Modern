import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const {
  applyAndroidGradleJvmArgs,
  ANDROID_GRADLE_JVM_ARGS,
} = require('../../plugins/withAndroidGradleJvmArgs.cjs') as {
  applyAndroidGradleJvmArgs: (properties: unknown[]) => unknown[];
  ANDROID_GRADLE_JVM_ARGS: string;
};
const smokeEmbed = require('../../plugins/smokeEmbed.cjs') as {
  applyAndroidSmokeGradleProperties: (properties: unknown[]) => unknown[];
  ANDROID_GRADLE_JVM_ARGS: string;
};

describe('Android Gradle JVM args', () => {
  it('replaces Expo\'s 2g/512m default with the shared 6g/1g floor', () => {
    const properties = [
      { type: 'comment', value: 'Project-wide Gradle settings.' },
      { type: 'property', key: 'org.gradle.jvmargs', value: '-Xmx2048m -XX:MaxMetaspaceSize=512m' },
      { type: 'property', key: 'android.useAndroidX', value: 'true' },
    ];

    const patched = applyAndroidGradleJvmArgs(properties) as {
      type: string;
      key?: string;
      value: string;
    }[];
    const jvmArgs = patched.filter((item) => item.key === 'org.gradle.jvmargs');

    assert.equal(jvmArgs.length, 1);
    assert.equal(jvmArgs[0]?.value, ANDROID_GRADLE_JVM_ARGS);
    assert.equal(ANDROID_GRADLE_JVM_ARGS, '-Xmx6g -XX:MaxMetaspaceSize=1g');
    assert.ok(patched.some((item) => item.key === 'android.useAndroidX'));
  });

  it('is registered by the dynamic Expo config for every Android prebuild', () => {
    const appConfig = readFileSync(new URL('../../app.config.ts', import.meta.url), 'utf8');
    assert.match(appConfig, /withAndroidGradleJvmArgs\.cjs/);
    assert.doesNotMatch(
      appConfig,
      /smokeEmbed \? \[[^\]]*withAndroidGradleJvmArgs/,
    );
  });

  it('is reused by smokeEmbed without a second heap-bump owner', () => {
    assert.equal(smokeEmbed.ANDROID_GRADLE_JVM_ARGS, ANDROID_GRADLE_JVM_ARGS);
    const properties = [{ type: 'property', key: 'org.gradle.jvmargs', value: '-Xmx2048m' }];
    assert.deepEqual(
      smokeEmbed.applyAndroidSmokeGradleProperties(properties),
      applyAndroidGradleJvmArgs(properties),
    );

    const smokeSource = readFileSync(new URL('../../plugins/smokeEmbed.cjs', import.meta.url), 'utf8');
    assert.match(smokeSource, /require\('\.\/withAndroidGradleJvmArgs\.cjs'\)/);
    assert.doesNotMatch(smokeSource, /withAndroidSmokeGradleProperties/);
  });
});
