import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { memberProfileFixture } from '../../test/fixtures.ts';
import { initialLegacySelection, legacyClaimStatus, settingsAccountView, settingsLegacyView } from './settingsPresentation.ts';

describe('settings account and legacy decisions', () => {
  const match = { userId: 47, username: 'Classic Fan' };
  it('selects the first claimable identity, then linked identity, then none', () => {
    const base = memberProfileFixture();
    assert.equal(initialLegacySelection({ ...base, legacyLink: { kind: 'claimable', match, claimableMatches: [{ userId: 12, username: 'First' }, match], unavailableMatches: [] } }), 12);
    assert.equal(initialLegacySelection({ ...base, legacyLink: { kind: 'linked', match, claimableMatches: [], unavailableMatches: [] } }), 47);
    assert.equal(initialLegacySelection(base), null);
  });
  it('shows linked and claimable sections only when their matching metadata is present', () => {
    for (const kind of ['none', 'linked', 'claimable', 'unavailable'] as const) {
      const profile = memberProfileFixture({ legacyLink: { kind, match, claimableMatches: [match], unavailableMatches: [] } });
      const view = settingsLegacyView(profile);
      assert.equal(view.linkedMatch, kind === 'linked' ? match : null);
      assert.equal(view.claimable, kind === 'claimable');
      assert.deepEqual(view.claimableMatches, [match]);
    }
    const missing = settingsLegacyView(memberProfileFixture({ legacyLink: { kind: 'linked', match: null, claimableMatches: [], unavailableMatches: [] } }));
    assert.equal(missing.linkedMatch, null);
    assert.equal(settingsLegacyView(memberProfileFixture({ legacyLink: { kind: 'claimable', match: null, claimableMatches: [], unavailableMatches: [] } })).claimable, false);
    assert.deepEqual(settingsLegacyView(null), { linkedMatch: null, claimable: false, claimableMatches: [] });
  });
  it('preserves limits, provider labels, deletion action, and claim result wording', () => {
    assert.deepEqual(settingsAccountView(null), { minDisplayNameLength: 2, maxDisplayNameLength: 100, linkedProviders: 'None linked', deletionLabel: 'Delete my account' });
    const profile = memberProfileFixture({ linkedProviders: ['Google', 'Apple'], scheduledDeletionAt: '2026-11-01', limits: { minDisplayNameLength: 3, maxDisplayNameLength: 80, maxAvatarBytes: 1, allowedAvatarContentTypes: [], deletionRetentionDays: 30 } });
    assert.deepEqual(settingsAccountView(profile), { minDisplayNameLength: 3, maxDisplayNameLength: 80, linkedProviders: 'Google, Apple', deletionLabel: 'Review or cancel deletion' });
    assert.equal(settingsAccountView(memberProfileFixture({ linkedProviders: [] })).linkedProviders, 'None linked');
    assert.equal(legacyClaimStatus(true), 'Legacy account claimed and display name updated.');
    assert.equal(legacyClaimStatus(false), 'Legacy account claimed.');
  });
});
