import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { createMemoryDownloadHost, opaqueFileName } from './files.ts';
import { trimTrailingChar } from '../text/trimRuns.ts';

describe('opaqueFileName', () => {
  it('keeps a safe id and optional extension', () => {
    assert.equal(opaqueFileName('perf-1', 'm4a'), 'perf-1.m4a');
    assert.equal(opaqueFileName('perf-1', 'part'), 'perf-1.part');
    assert.equal(opaqueFileName('perf-1', null), 'perf-1');
    assert.equal(opaqueFileName('perf 1/../x', 'm4a'), 'perf1x.m4a');
    assert.equal(opaqueFileName('', 'm4a'), '.m4a');
  });
});

describe('createMemoryDownloadHost join', () => {
  it('joins completed and part URIs on a single slash', () => {
    const host = createMemoryDownloadHost();
    assert.equal(host.completedUri('perf-1', 'm4a'), 'file:///documents/fan-performances/perf-1.m4a');
    assert.equal(host.partUri('perf-1'), 'file:///documents/fan-performances/perf-1.part');
    assert.equal(
      `${trimTrailingChar('file:///documents/fan-performances///', '/')}/perf-1.m4a`,
      'file:///documents/fan-performances/perf-1.m4a',
    );
  });

  it('finishes quickly when the root is a long slash run', () => {
    const started = performance.now();
    const root = `file:///documents/fan-performances${'/'.repeat(40_000)}`;
    assert.equal(`${trimTrailingChar(root, '/')}/perf-1.m4a`, 'file:///documents/fan-performances/perf-1.m4a');
    assert.ok(performance.now() - started < 100);
  });
});
