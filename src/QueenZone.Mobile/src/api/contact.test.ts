import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  buildContactSubmitBody,
  contactApiUrl,
  parseContactForm,
  parseContactSubmitResult,
} from './contact.ts';
import { readProblemDetail } from './problemDetail.ts';

describe('contactApiUrl', () => {
  it('joins the versioned contact path onto the API origin', () => {
    assert.equal(contactApiUrl('http://localhost:5146'), 'http://localhost:5146/api/v1/contact');
    assert.equal(contactApiUrl('http://localhost:5146/'), 'http://localhost:5146/api/v1/contact');
    assert.equal(contactApiUrl('http://localhost:5146///'), 'http://localhost:5146/api/v1/contact');
    assert.equal(contactApiUrl(''), '/api/v1/contact');
    assert.equal(contactApiUrl('   '), '   /api/v1/contact');
  });

  it('finishes quickly when the origin is a long slash run', () => {
    const started = performance.now();
    assert.equal(
      contactApiUrl(`http://localhost:5146${'/'.repeat(40_000)}`),
      'http://localhost:5146/api/v1/contact',
    );
    assert.ok(performance.now() - started < 100);
  });
});

describe('parseContactForm', () => {
  it('reads the public form contract', () => {
    const form = parseContactForm({
      signedIn: false,
      signedInDisplayName: null,
      requiresContactDetails: true,
      formStamp: 'stamp-1',
      intro: 'This form reaches the site admin.',
      confirmationTitle: 'Thank you',
      confirmationMessage:
        'Thanks — we have your message. The site admin will read it and reply by email if a response is needed.',
      topics: [{ value: 'Technical', label: 'Technical problem' }],
      limits: { minSubjectLength: 5, maxSubjectLength: 200, minMessageLength: 20 },
    });

    assert.equal(form.requiresContactDetails, true);
    assert.equal(form.formStamp, 'stamp-1');
    assert.equal(form.topics[0]?.label, 'Technical problem');
    assert.equal(form.limits.maxMessageLength, 4000);
    assert.match(form.confirmationMessage, /we have your message/);
  });
});

describe('buildContactSubmitBody', () => {
  it('omits guest fields when the member snapshot is in use', () => {
    const body = buildContactSubmitBody({
      topic: 'Account',
      subject: 'Please restore my display name',
      message: 'I changed my display name by mistake.',
      name: 'Ignored',
      email: 'ignored@example.com',
      formStamp: 'stamp-2',
      requiresContactDetails: false,
    });

    assert.equal(body.name, undefined);
    assert.equal(body.email, undefined);
    assert.equal(body.topic, 'Account');
  });
});

describe('parseContactSubmitResult', () => {
  it('requires submitted true and keeps confirmation copy', () => {
    const result = parseContactSubmitResult({
      submitted: true,
      confirmationTitle: 'Thank you',
      confirmationMessage:
        'Thanks — we have your message. The site admin will read it and reply by email if a response is needed.',
    });
    assert.equal(result.confirmationTitle, 'Thank you');
  });
});

describe('readProblemDetail', () => {
  it('prefers RFC 7807 detail', () => {
    assert.equal(readProblemDetail({ title: 'Bad Request', detail: 'Name is required.' }, 'fallback'), 'Name is required.');
    assert.equal(readProblemDetail({ title: 'Unauthorized' }, 'fallback'), 'Unauthorized');
    assert.equal(readProblemDetail(null, 'fallback'), 'fallback');
  });
});
