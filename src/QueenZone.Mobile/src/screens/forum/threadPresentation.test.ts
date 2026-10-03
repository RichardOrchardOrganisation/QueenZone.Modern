import assert from 'node:assert/strict';
import { it } from 'node:test';
import { threadHeading } from './threadPresentation.ts';

it('preserves loaded forum heading and plural post count', () => {
  assert.deepEqual(threadHeading({ title: 'Live Magic', forumName: 'Queen', postCount: 2 }, 'Route'), { title: 'Live Magic', forumName: 'Queen', stats: '2 posts · Queen' });
  assert.equal(threadHeading({ title: '', forumName: '', postCount: 1 }, undefined).title, '');
  assert.equal(threadHeading({ title: 'One', forumName: 'Forum', postCount: 1 }, undefined).stats, '1 posts · Forum');
});
it('uses route and default headings while details are unavailable', () => {
  assert.deepEqual(threadHeading(null, 'Route'), { title: 'Route', forumName: 'Forum', stats: null });
  assert.deepEqual(threadHeading(null, undefined), { title: 'Thread', forumName: 'Forum', stats: null });
  assert.equal(threadHeading(null, '').title, '');
});
