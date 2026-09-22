/**
 * Complete Expo's release-only R8 setup.
 *
 * expo-build-properties enables minification and resource shrinking, but the
 * generated React Native template still selects proguard-android.txt, which
 * disables R8's code optimisation passes. AGP 8.12 also requires the Gradle
 * property below to opt into the integrated resource shrinker (AGP 9 enables
 * it automatically).
 */
const {
  createRunOncePlugin,
  withAppBuildGradle,
  withGradleProperties,
} = require('expo/config-plugins');

const LEGACY_RULES = 'getDefaultProguardFile("proguard-android.txt")';
const OPTIMIZING_RULES = 'getDefaultProguardFile("proguard-android-optimize.txt")';
const OPTIMIZED_RESOURCE_SHRINKING = 'android.r8.optimizedResourceShrinking';

function applyOptimizingDefaultRules(contents) {
  if (contents.includes(OPTIMIZING_RULES)) {
    return contents;
  }
  if (!contents.includes(LEGACY_RULES)) {
    throw new Error(
      'Unable to enable full R8 optimisation: Expo generated an unexpected proguardFiles declaration.',
    );
  }
  return contents.replace(LEGACY_RULES, OPTIMIZING_RULES);
}

function applyOptimizedResourceShrinking(properties) {
  const existing = properties.find(
    (property) => property.type === 'property' && property.key === OPTIMIZED_RESOURCE_SHRINKING,
  );
  if (existing) {
    existing.value = 'true';
    return properties;
  }
  return [
    ...properties,
    {
      type: 'property',
      key: OPTIMIZED_RESOURCE_SHRINKING,
      value: 'true',
    },
  ];
}

function withAndroidR8Optimization(config) {
  config = withGradleProperties(config, (mod) => {
    mod.modResults = applyOptimizedResourceShrinking(mod.modResults);
    return mod;
  });
  return withAppBuildGradle(config, (mod) => {
    mod.modResults.contents = applyOptimizingDefaultRules(mod.modResults.contents);
    return mod;
  });
}

const plugin = createRunOncePlugin(
  withAndroidR8Optimization,
  'withAndroidR8Optimization',
  '1.0.0',
);

plugin.applyOptimizingDefaultRules = applyOptimizingDefaultRules;
plugin.applyOptimizedResourceShrinking = applyOptimizedResourceShrinking;
module.exports = plugin;
