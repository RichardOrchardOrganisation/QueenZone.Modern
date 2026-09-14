import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ARCHIVE_HUB_IDS, archiveDestinations } from '../content/archiveHub.ts';
import {
  getVisibleTabNames,
  isMemberOnlyScreen,
  publicTabNames,
  shouldHideTabBar,
  signedInOnlyTabNames,
} from './visibility.ts';

describe('getVisibleTabNames', () => {
  it('keeps the five website-mirroring tabs whether signed in or out', () => {
    const expected = ['HomeTab', 'NewsTab', 'PhotosTab', 'ArchiveTab', 'ForumTab'];
    assert.deepEqual([...publicTabNames], expected);
    assert.deepEqual([...getVisibleTabNames(false)], expected);
    assert.deepEqual([...getVisibleTabNames(true)], expected);
    assert.equal(signedInOnlyTabNames.length, 0);
  });
});

describe('member-only screens', () => {
  it('gates private messages, compose, and account settings', () => {
    assert.equal(isMemberOnlyScreen('Inbox'), true);
    assert.equal(isMemberOnlyScreen('Conversation'), true);
    assert.equal(isMemberOnlyScreen('Composer'), true);
    assert.equal(isMemberOnlyScreen('Settings'), true);
    assert.equal(isMemberOnlyScreen('Appearance'), false);
    assert.equal(isMemberOnlyScreen('DeleteAccount'), true);
    assert.equal(isMemberOnlyScreen('PhotoSubmit'), true);
    assert.equal(isMemberOnlyScreen('FanPerformanceSubmit'), true);
    assert.equal(isMemberOnlyScreen('FanPerformanceDownloads'), true);
    assert.equal(isMemberOnlyScreen('MySubmissions'), true);
    assert.equal(isMemberOnlyScreen('SavedList'), true);
  });

  it('keeps archive, photos, forum browse, profile, and contact public', () => {
    assert.equal(isMemberOnlyScreen('Home'), false);
    assert.equal(isMemberOnlyScreen('ArchiveHub'), false);
    assert.equal(isMemberOnlyScreen('NewsIndex'), false);
    assert.equal(isMemberOnlyScreen('PhotoIndex'), false);
    assert.equal(isMemberOnlyScreen('PhotoCategory'), false);
    assert.equal(isMemberOnlyScreen('ForumIndex'), false);
    assert.equal(isMemberOnlyScreen('Category'), false);
    assert.equal(isMemberOnlyScreen('Thread'), false);
    assert.equal(isMemberOnlyScreen('FanPerformances'), false);
    assert.equal(isMemberOnlyScreen('FanPerformanceDetail'), false);
    assert.equal(isMemberOnlyScreen('Trivia'), false);
    assert.equal(isMemberOnlyScreen('Contact'), false);
    assert.equal(isMemberOnlyScreen('Profile'), false);
    assert.equal(isMemberOnlyScreen('SuggestNews'), false);
  });
});

describe('shouldHideTabBar', () => {
  it('hides the tab bar on immersive and pushed-detail routes', () => {
    for (const name of [
      'Story',
      'Quote',
      'TimelineEvent',
      'BiographyChapter',
      'Album',
      'Thread',
      'PhotoViewer',
      'Profile',
      'DeleteAccount',
      'Contact',
      'MySubmissions',
      'Search',
      'FanPerformanceDetail',
      'Conversation',
      'Composer',
      'SuggestNews',
      'FanPerformanceSubmit',
      'FanPerformanceDownloads',
    ]) {
      assert.equal(shouldHideTabBar(name), true, name);
    }
  });

  it('keeps the tab bar on tab roots and archive section lists', () => {
    for (const name of [
      'Home',
      'NewsIndex',
      'ArchiveHub',
      'PhotoIndex',
      'PhotoCategory',
      'ForumIndex',
      'Category',
      'Articles',
      'Biography',
      'Discography',
      'Timeline',
      'FreddieTribute',
      'FanPerformances',
      'Trivia',
      'AboutArchive',
    ]) {
      assert.equal(shouldHideTabBar(name), false, name);
    }
  });
});

describe('archive hub destinations', () => {
  it('lists the archive hub rows including Trivia', () => {
    assert.deepEqual([...ARCHIVE_HUB_IDS], [
      'stories',
      'timeline',
      'biography',
      'discography',
      'tribute',
      'fan-performances',
      'recently-restored',
      'trivia',
      'about',
    ]);
    assert.equal(ARCHIVE_HUB_IDS.length, 9);
    assert.deepEqual(
      archiveDestinations.map((row) => row.id),
      [...ARCHIVE_HUB_IDS],
    );
  });
});
