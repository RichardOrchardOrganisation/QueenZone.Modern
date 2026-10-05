/**
 * Stop expo-audio iOS from calling AVPlayerItem.currentDate() on every status
 * tick (#1234 / QUEENZONE-MOBILE-7).
 *
 * currentStatus() is invoked from addPeriodicTimeObserver on the main queue
 * (queue: nil) at updateInterval 250. The currentOffsetFromLive getter runs
 * currentDate() → sync XPC and can AppHang ≥2000ms. QueenZone never reads
 * that field (Bearer VOD, not HLS live). Do not gate on isLive —
 * duration.isIndefinite is true while duration is still unknown at start.
 *
 * Patches node_modules/expo-audio/ios/AudioPlayer.swift at prebuild.
 * currentTime stays on currentTime(). Android is unchanged.
 */
const fs = require('node:fs');
const path = require('node:path');
const { createRunOncePlugin, withDangerousMod } = require('expo/config-plugins');

const TAG = 'queenzone-expo-audio-ios-current-offset-from-live';
const AUDIO_PLAYER_RELATIVE_PATH = path.join('node_modules', 'expo-audio', 'ios', 'AudioPlayer.swift');
const STATUS_OFFSET_ASSIGNMENT = /("currentOffsetFromLive"\s*:\s*)currentOffsetFromLive(\s*,)/;
const PATCHED_OFFSET_ASSIGNMENT = /"currentOffsetFromLive"\s*:\s*nil\s*,/;

function applyCurrentOffsetFromLiveNil(contents) {
  if (PATCHED_OFFSET_ASSIGNMENT.test(contents) && !STATUS_OFFSET_ASSIGNMENT.test(contents)) {
    return contents;
  }

  if (!STATUS_OFFSET_ASSIGNMENT.test(contents)) {
    throw new Error(
      'expo-audio AudioPlayer.swift currentStatus() no longer assigns currentOffsetFromLive — template may have changed.',
    );
  }

  return contents.replace(STATUS_OFFSET_ASSIGNMENT, '$1nil$2');
}

function patchExpoAudioAudioPlayerSwift(projectRoot) {
  const filePath = path.join(projectRoot, AUDIO_PLAYER_RELATIVE_PATH);
  if (!fs.existsSync(filePath)) {
    throw new Error(`Missing ${AUDIO_PLAYER_RELATIVE_PATH} — expo-audio must be installed before this plugin.`);
  }

  const next = applyCurrentOffsetFromLiveNil(fs.readFileSync(filePath, 'utf8'));
  fs.writeFileSync(filePath, next);
  return filePath;
}

function withExpoAudioIosCurrentOffsetFromLive(config) {
  return withDangerousMod(config, [
    'ios',
    (mod) => {
      patchExpoAudioAudioPlayerSwift(mod.modRequest.projectRoot);
      return mod;
    },
  ]);
}

const plugin = createRunOncePlugin(
  withExpoAudioIosCurrentOffsetFromLive,
  'withExpoAudioIosCurrentOffsetFromLive',
  '1.0.0',
);

plugin.applyCurrentOffsetFromLiveNil = applyCurrentOffsetFromLiveNil;
plugin.patchExpoAudioAudioPlayerSwift = patchExpoAudioAudioPlayerSwift;
plugin.AUDIO_PLAYER_RELATIVE_PATH = AUDIO_PLAYER_RELATIVE_PATH;
plugin.TAG = TAG;
module.exports = plugin;
