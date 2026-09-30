import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { test } from 'node:test';

const require = createRequire(import.meta.url);
const { applyAppIntents } = require('../../plugins/withIosAppIntents.cjs') as {
  applyAppIntents: (contents: string, apiBaseUrl: string) => string;
};

const appDelegate = `internal import Expo
class AppDelegate {
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
}`;

test('App Intents CNG plugin injects the public API URL and shortcut registration once', () => {
  const first = applyAppIntents(appDelegate, 'https://www.queenzone.org');
  assert.match(first, /import AppIntents/);
  assert.match(first, /QueenZoneAppShortcuts.updateAppShortcutParameters\(\)/);
  assert.match(first, /static let base = "https:\/\/www.queenzone.org"/);
  assert.match(first, /struct QueenZoneTodayIntent/);
  assert.match(first, /struct QueenZoneNewsIntent/);
  assert.match(first, /struct QueenZoneTriviaIntent/);
  assert.match(first, /struct QueenZoneSearchIntent/);
  assert.match(first, /struct QueenZoneArchiveEntity: IndexedEntity/);
  assert.match(first, /QueenZoneArchiveCatalog.refreshSpotlight\(\)/);
  assert.equal(applyAppIntents(first, 'https://www.queenzone.org'), first);
});

test('App Intents CNG plugin rejects unexpected generated AppDelegate shape', () => {
  assert.throws(() => applyAppIntents('class AppDelegate {}', 'https://www.queenzone.org'));
});
