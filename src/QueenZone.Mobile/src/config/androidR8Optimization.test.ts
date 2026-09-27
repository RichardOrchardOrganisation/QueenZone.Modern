import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const {
  applyOptimizingDefaultRules,
  applyOptimizedResourceShrinking,
} = require('../../plugins/withAndroidR8Optimization.cjs') as {
  applyOptimizingDefaultRules: (contents: string) => string;
  applyOptimizedResourceShrinking: (
    properties: { type: string; key?: string; value?: string }[],
  ) => { type: string; key?: string; value?: string }[];
};

describe('Android release R8 optimization', () => {
  it('selects the optimizing Android defaults idempotently', () => {
    const generated =
      'proguardFiles getDefaultProguardFile("proguard-android.txt"), "proguard-rules.pro"';
    const optimized = applyOptimizingDefaultRules(generated);

    assert.match(optimized, /proguard-android-optimize\.txt/);
    assert.equal(applyOptimizingDefaultRules(optimized), optimized);
  });

  it('fails closed if the generated Gradle template changes unexpectedly', () => {
    assert.throws(
      () => applyOptimizingDefaultRules('proguardFiles customRules'),
      /unexpected proguardFiles declaration/,
    );
  });

  it('enables optimized resource shrinking once', () => {
    const first = applyOptimizedResourceShrinking([{ type: 'comment', value: 'generated' }]);
    const second = applyOptimizedResourceShrinking(first);

    assert.equal(
      second.filter((entry) => entry.key === 'android.r8.optimizedResourceShrinking').length,
      1,
    );
    assert.equal(second.at(-1)?.value, 'true');
  });

  it('is registered by the dynamic Expo config', () => {
    const appConfig = readFileSync(new URL('../../app.config.ts', import.meta.url), 'utf8');
    assert.match(appConfig, /withAndroidR8Optimization\.cjs/);
  });
});
