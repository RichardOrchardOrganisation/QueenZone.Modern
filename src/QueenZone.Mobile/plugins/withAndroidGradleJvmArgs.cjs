/**
 * Always-on Gradle JVM floor for every Android CNG prebuild (#1709).
 *
 * Release packaging (assembleRelease / bundleRelease / :app:minifyReleaseWithR8)
 * exceeds Expo/AGP's generated 2 GiB heap and 512 MiB metaspace. Play publish
 * run 35821476038 OOMed on :app:minifyReleaseWithR8 during bundleRelease while
 * store CNG still used those defaults. smokeEmbed already applied
 * `-Xmx6g -XX:MaxMetaspaceSize=1g` for smoke-only builds; keep that floor here
 * so store/Play prebuilds match. Do not rely on a Play-workflow-only env
 * override or a larger runner as the first durable fix.
 */
const { createRunOncePlugin, withGradleProperties } = require('expo/config-plugins');

const ANDROID_GRADLE_JVM_ARGS = '-Xmx6g -XX:MaxMetaspaceSize=1g';

function applyAndroidGradleJvmArgs(properties) {
  const list = Array.isArray(properties) ? properties : [];
  const next = list.filter(
    (item) => !(item?.type === 'property' && item.key === 'org.gradle.jvmargs'),
  );
  next.push({
    type: 'property',
    key: 'org.gradle.jvmargs',
    value: ANDROID_GRADLE_JVM_ARGS,
  });
  return next;
}

function withAndroidGradleJvmArgs(config) {
  return withGradleProperties(config, (mod) => {
    mod.modResults = applyAndroidGradleJvmArgs(mod.modResults);
    return mod;
  });
}

const plugin = createRunOncePlugin(
  withAndroidGradleJvmArgs,
  'withAndroidGradleJvmArgs',
  '1.0.0',
);

plugin.applyAndroidGradleJvmArgs = applyAndroidGradleJvmArgs;
plugin.ANDROID_GRADLE_JVM_ARGS = ANDROID_GRADLE_JVM_ARGS;
module.exports = plugin;
