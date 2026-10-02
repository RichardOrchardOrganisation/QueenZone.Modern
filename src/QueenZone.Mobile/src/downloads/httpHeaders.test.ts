import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { parseContentRangeTotal } from './httpHeaders.ts';
describe('Content-Range total', () => {
  for (const [header, total] of [
    [null, null], ['', null], ['bytes 0-0/12345', 12345], ['bytes 0-0/*', null],
    ['bytes 0-0/0', null], ['bytes 0-0/abc', null], ['bytes 0-0/', null],
    ['bytes 0-0/12345 \t', 12345], [`bytes 0-0/${'9'.repeat(400)}`, null],
  ] as const) {
    it(`parses ${header === null ? 'missing' : JSON.stringify(header)}`, () => {
      assert.equal(parseContentRangeTotal(header), total);
    });
  }
});
