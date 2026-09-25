/** Add source-controlled App Intents to Expo SDK 57's generated app target. */
const fs = require('fs');
const path = require('path');
const { createRunOncePlugin, withAppDelegate } = require('expo/config-plugins');

const TAG = 'queenzone-ios-app-intents';
const SOURCE = path.join(__dirname, 'ios', 'QueenZoneAppIntents.swift');

function applyAppIntents(contents, apiBaseUrl) {
  const url = new URL(apiBaseUrl);
  if (!['https:', 'http:'].includes(url.protocol)) throw new Error('Invalid App Intents API URL');
  const source = fs.readFileSync(SOURCE, 'utf8').replace('__QUEENZONE_API_BASE_URL__', url.origin);
  const begin = `// @generated begin ${TAG}`;
  const end = `// @generated end ${TAG}`;
  if (contents.includes(begin)) {
    const start = contents.indexOf(begin);
    const finish = contents.indexOf(end, start);
    if (finish < 0) throw new Error('App Intents generated marker is incomplete');
    return contents.slice(0, start) + `${begin}\n${source.trim()}\n${end}` + contents.slice(finish + end.length);
  }
  if (!contents.includes('import Expo')) {
    throw new Error('Expo Swift AppDelegate template changed; review App Intents insertion.');
  }
  const launchMarker = '    return super.application(application, didFinishLaunchingWithOptions: launchOptions)';
  if (!contents.includes(launchMarker)) throw new Error('Expo AppDelegate launch marker changed');
  return contents.replace('import Expo', 'import Expo\nimport AppIntents')
    .replace(launchMarker, `    QueenZoneAppShortcuts.updateAppShortcutParameters()
    if #available(iOS 18.4, *) {
      Task { await QueenZoneArchiveCatalog.refreshSpotlight() }
    }
${launchMarker}`)
    + `\n${begin}\n${source.trim()}\n${end}\n`;
}

function withIosAppIntents(config, options) {
  return withAppDelegate(config, (mod) => {
    if (mod.modResults.language !== 'swift') throw new Error('App Intents require Swift AppDelegate');
    mod.modResults.contents = applyAppIntents(mod.modResults.contents, options.apiBaseUrl);
    return mod;
  });
}

const plugin = createRunOncePlugin(withIosAppIntents, TAG, '1.0.0');
plugin.applyAppIntents = applyAppIntents;
module.exports = plugin;
