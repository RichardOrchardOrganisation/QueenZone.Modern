import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  avatarUrl,
  formatMemberSince,
  parseDeletionRequested,
  parseMemberProfile,
  validateDisplayName,
} from './me.ts';

const sampleProfile = {
  memberId: '11111111-1111-1111-1111-111111111111',
  email: 'fan@example.com',
  displayName: 'Roger O',
  createdAt: '2004-06-01T00:00:00Z',
  lastLoginAt: null,
  hasAvatar: true,
  avatarPath: '/account/avatar/11111111-1111-1111-1111-111111111111',
  avatarThumbPath: '/account/avatar/11111111-1111-1111-1111-111111111111?size=thumb',
  messagePrivacy: 'followed',
  linkedProviders: ['Google'],
  legacyLink: {
    kind: 'linked',
    match: { userId: 42, username: 'ClassicFan' },
    claimableMatches: [],
    unavailableMatches: [],
  },
  scheduledDeletionAt: null,
  limits: {
    minDisplayNameLength: 2,
    maxDisplayNameLength: 100,
    maxAvatarBytes: 2097152,
    allowedAvatarContentTypes: ['image/jpeg', 'image/png', 'image/webp'],
    deletionRetentionDays: 30,
  },
  deletion: {
    confirmationPhrase: 'DELETE',
    confirmationHint: 'Type DELETE to schedule deletion of the account.',
    requestedTitle: 'Account deletion scheduled',
    requestedMessage: 'You have been signed out.',
    whatHappens: ['You are signed out after requesting deletion.'],
  },
};

describe('parseMemberProfile', () => {
  it('reads the account settings contract', () => {
    const profile = parseMemberProfile(sampleProfile);
    assert.equal(profile.displayName, 'Roger O');
    assert.equal(profile.messagePrivacy, 'followed');
    assert.equal(profile.legacyLink.kind, 'linked');
    assert.equal(profile.legacyLink.match?.userId, 42);
    assert.equal(profile.limits.minDisplayNameLength, 2);
    assert.equal(profile.deletion.confirmationPhrase, 'DELETE');
  });

  it('rejects a payload missing member identity', () => {
    assert.throws(() => parseMemberProfile(null), /Profile response was empty\./);
    assert.throws(() => parseMemberProfile({ memberId: 'm1' }), /missing member identity/);
  });

  it('falls back to defaults when optional fields are missing or invalid', () => {
    const profile = parseMemberProfile({
      memberId: 'm1',
      email: 'fan@example.com',
      displayName: 'Roger',
      messagePrivacy: 'not-a-real-value',
      legacyLink: { kind: 'not-a-real-kind', match: { userId: 'nope', username: 'x' } },
      limits: {},
      deletion: {},
    });

    assert.equal(profile.createdAt, '');
    assert.equal(profile.lastLoginAt, null);
    assert.equal(profile.hasAvatar, false);
    assert.equal(profile.avatarPath, null);
    assert.equal(profile.avatarThumbPath, null);
    assert.equal(profile.messagePrivacy, 'members');
    assert.deepEqual(profile.linkedProviders, []);
    assert.equal(profile.legacyLink.kind, 'none');
    assert.equal(profile.legacyLink.match, null);
    assert.deepEqual(profile.legacyLink.claimableMatches, []);
    assert.equal(profile.scheduledDeletionAt, null);
    assert.deepEqual(profile.limits, {
      minDisplayNameLength: 2,
      maxDisplayNameLength: 100,
      maxAvatarBytes: 2 * 1024 * 1024,
      allowedAvatarContentTypes: ['image/jpeg', 'image/png', 'image/webp'],
      deletionRetentionDays: 30,
    });
    assert.equal(profile.deletion.confirmationPhrase, 'DELETE');
    assert.equal(
      profile.deletion.requestedMessage,
      'You have been signed out. You can sign back in and cancel deletion during the 30-day cooling-off period.',
    );
    assert.deepEqual(profile.deletion.whatHappens, []);
  });

  it('filters non-string entries out of linkedProviders and legacy match lists', () => {
    const profile = parseMemberProfile({
      memberId: 'm1',
      email: 'fan@example.com',
      displayName: 'Roger',
      linkedProviders: ['Google', 42, null],
      legacyLink: {
        claimableMatches: [{ userId: 1, username: 'a' }, { userId: 'bad' }, 'not-an-object'],
      },
    });

    assert.deepEqual(profile.linkedProviders, ['Google']);
    assert.deepEqual(profile.legacyLink.claimableMatches, [{ userId: 1, username: 'a' }]);
  });
});

describe('validateDisplayName', () => {
  it('matches website length rules', () => {
    assert.equal(validateDisplayName('A'), 'Display name must be at least 2 characters.');
    assert.equal(validateDisplayName('  '), 'Display name is required.');
    assert.equal(validateDisplayName('Roger'), null);
    assert.equal(
      validateDisplayName('x'.repeat(101)),
      'Display name must be at most 100 characters.',
    );
  });
});

describe('avatarUrl', () => {
  it('joins the website serve path onto the API origin', () => {
    assert.equal(
      avatarUrl('http://localhost:5146', '/account/avatar/abc'),
      'http://localhost:5146/account/avatar/abc',
    );
    assert.equal(
      avatarUrl('http://localhost:5146/', '/account/avatar/abc', '1'),
      'http://localhost:5146/account/avatar/abc?v=1',
    );
    assert.equal(avatarUrl('http://localhost:5146', null), null);
    assert.equal(avatarUrl('', '/account/avatar/abc'), '/account/avatar/abc');
    assert.equal(
      avatarUrl('http://localhost:5146///', 'account/avatar/abc'),
      'http://localhost:5146/account/avatar/abc',
    );
  });

  it('finishes quickly when the origin is a long slash run', () => {
    const started = performance.now();
    assert.equal(
      avatarUrl(`http://localhost:5146${'/'.repeat(40_000)}`, '/account/avatar/abc'),
      'http://localhost:5146/account/avatar/abc',
    );
    assert.ok(performance.now() - started < 100);
  });
});

describe('formatMemberSince', () => {
  it('formats month and year', () => {
    assert.match(formatMemberSince('2004-06-01T00:00:00Z'), /June 2004/);
  });
});

describe('parseDeletionRequested', () => {
  it('accepts an immediate deletion with no scheduled date', () => {
    const result = parseDeletionRequested({ requested: true, scheduledDeletionAt: null, statusReceipt: 'opaque' });
    assert.equal(result.scheduledDeletionAt, null);
    assert.equal(result.statusReceipt, 'opaque');
    assert.equal(result.title, 'Account deletion requested');
    assert.match(result.message, /being removed/);
  });

  it('requires requested true', () => {
    const result = parseDeletionRequested({
      requested: true,
      scheduledDeletionAt: '2026-09-21T00:00:00Z',
      title: 'Account deletion scheduled',
      message: 'You have been signed out.',
    });
    assert.equal(result.title, 'Account deletion scheduled');
  });

  it('defaults the title and message when the payload omits them', () => {
    const result = parseDeletionRequested({
      requested: true,
      scheduledDeletionAt: '2026-09-21T00:00:00Z',
    });
    assert.equal(result.title, 'Account deletion scheduled');
    assert.match(result.message, /cooling-off period/);
  });

  it('rejects a payload that was not confirmed', () => {
    assert.throws(() => parseDeletionRequested(null), /Deletion response was empty\./);
    assert.throws(
      () => parseDeletionRequested({ requested: false, scheduledDeletionAt: '2026-09-21T00:00:00Z' }),
      /Deletion was not confirmed\./,
    );
    assert.throws(
      () => parseDeletionRequested({ requested: true }),
      /Deletion was not confirmed\./,
    );
  });
});
