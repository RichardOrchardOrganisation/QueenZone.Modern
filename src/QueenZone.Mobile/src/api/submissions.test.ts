import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  memberAuthHeaders,
  parseArticleSubmissions,
  parseFanPerformanceSubmissions,
  parseNewsSuggestions,
  parsePhotoSubmissions,
  resolveMediaUrl,
  submissionsApiUrl,
} from './submissions.ts';
import { readProblemDetail } from './problemDetail.ts';

describe('submissionsApiUrl', () => {
  it('joins the versioned member submissions path onto the API origin', () => {
    assert.equal(
      submissionsApiUrl('http://localhost:5146', 'photos', 2, 20),
      'http://localhost:5146/api/v1/me/submissions/photos?page=2&pageSize=20',
    );
    assert.equal(
      submissionsApiUrl('http://localhost:5146/', 'news'),
      'http://localhost:5146/api/v1/me/submissions/news?page=1&pageSize=20',
    );
  });
});

describe('memberAuthHeaders', () => {
  it('adds a bearer token when one is present', () => {
    assert.deepEqual(memberAuthHeaders(null), { Accept: 'application/json' });
    assert.deepEqual(memberAuthHeaders('abc'), {
      Accept: 'application/json',
      Authorization: 'Bearer abc',
    });
  });
});

describe('resolveMediaUrl', () => {
  it('prefixes relative UGC paths with the API origin', () => {
    assert.equal(
      resolveMediaUrl('http://localhost:5146', '/ugc/photos/members/a/thumb.webp'),
      'http://localhost:5146/ugc/photos/members/a/thumb.webp',
    );
    assert.equal(resolveMediaUrl('http://localhost:5146', null), null);
  });
});

describe('parsePhotoSubmissions', () => {
  it('reads status, notes, and thumbnail from the paged contract', () => {
    const page = parsePhotoSubmissions({
      items: [
        {
          id: '11111111-1111-1111-1111-111111111111',
          title: 'Live at Wembley',
          submittedAt: '2026-08-01T12:00:00Z',
          status: { status: 'Rejected', statusLabel: 'Rejected', statusTone: 'danger' },
          notes: 'Too dark',
          thumbnailPath: '/ugc/photos/members/a/thumb.webp',
          promotedPicId: null,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });

    assert.equal(page.items[0]?.title, 'Live at Wembley');
    assert.equal(page.items[0]?.status.status, 'Rejected');
    assert.equal(page.items[0]?.notes, 'Too dark');
    assert.equal(page.totalCount, 1);
  });
});

describe('parseNewsSuggestions', () => {
  it('keeps the published path after admin promotion', () => {
    const page = parseNewsSuggestions({
      items: [
        {
          id: '22222222-2222-2222-2222-222222222222',
          url: 'https://example.com/queen-story',
          truncatedUrl: 'https://example.com/queen-story',
          title: 'Owner news',
          submittedAt: '2026-08-01T12:00:00Z',
          status: { status: 'Promoted', statusLabel: 'Promoted', statusTone: 'success' },
          notes: 'Promoted',
          publishedNewsId: 1003,
          publishedPath: '/news/1003/queenzone-modernisation-begins',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });

    assert.equal(page.items[0]?.publishedNewsId, 1003);
    assert.match(page.items[0]?.publishedPath ?? '', /\/news\/1003\//);
  });
});

describe('parseArticleSubmissions', () => {
  it('marks drafts as editable', () => {
    const page = parseArticleSubmissions({
      items: [
        {
          id: '33333333-3333-3333-3333-333333333333',
          title: 'Fan essay',
          submittedAt: null,
          status: { status: 'Draft', statusLabel: 'Draft', statusTone: 'pending' },
          notes: null,
          canContinueEditing: true,
          editPath: '/submit/article/33333333-3333-3333-3333-333333333333',
          publishedPath: null,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });

    assert.equal(page.items[0]?.canContinueEditing, true);
    assert.equal(page.items[0]?.status.statusTone, 'pending');
  });
});

describe('parseFanPerformanceSubmissions', () => {
  it('reads notes, rejection reason, and published path', () => {
    const page = parseFanPerformanceSubmissions({
      items: [
        {
          id: '44444444-4444-4444-4444-444444444444',
          title: 'Reaching Out cover',
          coveredSong: 'Reaching Out',
          performedBy: 'Stage Fan',
          submittedAt: '2026-09-01T12:00:00Z',
          status: { status: 'Approved', statusLabel: 'Approved', statusTone: 'success' },
          notes: null,
          rejectionReason: null,
          promotedStageId: 187,
          publishedPath: '/fan-performances#fan-performance-187',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });

    assert.equal(page.items[0]?.promotedStageId, 187);
    assert.match(page.items[0]?.publishedPath ?? '', /fan-performance-187/);
  });
});

describe('readProblemDetail', () => {
  it('prefers RFC 7807 detail', () => {
    assert.equal(readProblemDetail({ title: 'Unauthorized', detail: 'The access token is invalid or expired.' }, 'fallback'), 'The access token is invalid or expired.');
    assert.equal(readProblemDetail({ title: 'Unauthorized' }, 'fallback'), 'Unauthorized');
    assert.equal(readProblemDetail(null, 'fallback'), 'fallback');
  });
});
