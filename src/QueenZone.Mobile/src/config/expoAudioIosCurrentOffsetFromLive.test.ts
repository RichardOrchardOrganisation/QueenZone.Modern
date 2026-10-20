import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { applyCurrentOffsetFromLiveNil, AUDIO_PLAYER_RELATIVE_PATH } = require(
  '../../plugins/withExpoAudioIosCurrentOffsetFromLive.cjs',
) as {
  applyCurrentOffsetFromLiveNil: (contents: string) => string;
  AUDIO_PLAYER_RELATIVE_PATH: string;
};

const mobileRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

const expoAudio58CurrentStatus = `  var currentTime: Double {
    let seconds = ref.currentItem?.currentTime().seconds ?? 0.0
    return seconds.isNaN ? 0.0 : seconds
  }

  var isLive: Bool {
    ref.currentItem?.duration.isIndefinite ?? false
  }

  var currentOffsetFromLive: Double? {
    guard let currentDate = ref.currentItem?.currentDate() else {
      return nil
    }
    return Date().timeIntervalSince1970 - currentDate.timeIntervalSince1970
  }

  func currentStatus(knownCurrentTime: Double? = nil) -> [String: Any?] {
    let currentDuration = ref.status == .readyToPlay ? duration : 0.0
    let rate = isPlaying ? ref.rate : currentRate
    return [
      "id": id,
      "currentTime": knownCurrentTime ?? currentTime,
      "playbackState": statusToString(status: ref.status),
      "timeControlStatus": timeControlStatusString(status: ref.timeControlStatus),
      "reasonForWaitingToPlay": reasonForWaitingToPlayString(status: ref.reasonForWaitingToPlay),
      "mute": ref.isMuted,
      "duration": currentDuration,
      "playing": isPlaying,
      "loop": isLooping,
      "didJustFinish": false,
      "isLoaded": isLoaded,
      "playbackRate": rate,
      "shouldCorrectPitch": shouldCorrectPitch,
      "isBuffering": isBuffering,
      "isLive": isLive,
      "currentOffsetFromLive": currentOffsetFromLive,
      "error": nil
    ]
  }

  func setActiveForLockScreen(_ active: Bool = true, metadata: Metadata? = nil, options: LockScreenOptions?) {
    self.metadata = metadata
  }
`;

function extractCurrentStatus(source: string): string {
  const start = source.indexOf('func currentStatus(knownCurrentTime: Double? = nil)');
  assert.notEqual(start, -1, 'expected currentStatus()');
  const end = source.indexOf('\n  func ', start + 1);
  return source.slice(start, end === -1 ? undefined : end);
}

describe('applyCurrentOffsetFromLiveNil', () => {
  it('makes currentStatus send nil without calling currentDate, and keeps currentTime', () => {
    const first = applyCurrentOffsetFromLiveNil(expoAudio58CurrentStatus);
    const status = extractCurrentStatus(first);

    assert.match(status, /"currentOffsetFromLive": nil,/);
    assert.equal(status.includes('currentDate'), false);
    assert.equal(status.includes('currentOffsetFromLive,'), false);
    assert.match(status, /"currentTime": knownCurrentTime \?\? currentTime,/);
    assert.match(first, /currentTime\(\)\.seconds/);
    assert.equal(status.includes('isLive ?'), false);
    assert.equal(status.includes('if isLive'), false);

    const second = applyCurrentOffsetFromLiveNil(first);
    assert.equal(second, first);
  });

  it('fails closed when currentStatus no longer assigns currentOffsetFromLive', () => {
    assert.throws(
      () => applyCurrentOffsetFromLiveNil('func currentStatus(knownCurrentTime: Double? = nil) -> [String: Any?] { return [:] }'),
      /template may have changed/,
    );
  });

  it('matches installed expo-audio 58 AudioPlayer.currentStatus', () => {
    const installed = path.join(mobileRoot, AUDIO_PLAYER_RELATIVE_PATH);
    assert.equal(existsSync(installed), true, `expected ${AUDIO_PLAYER_RELATIVE_PATH} after npm ci`);
    const patched = applyCurrentOffsetFromLiveNil(readFileSync(installed, 'utf8'));
    const status = extractCurrentStatus(patched);
    assert.match(status, /"currentOffsetFromLive": nil,/);
    assert.equal(status.includes('currentDate'), false);
    assert.match(status, /"currentTime": knownCurrentTime \?\? currentTime,/);
    assert.equal((patched.match(/currentOffsetFromLive/g) ?? []).length, 2, 'only the getter and nil status field remain');
    const playlist = readFileSync(path.join(mobileRoot, 'node_modules/expo-audio/ios/AudioPlaylist.swift'), 'utf8');
    assert.doesNotMatch(playlist, /currentOffsetFromLive|currentDate\(/);
  });
});

describe('JS playback still uses currentTime', () => {
  it('FanPerformancePlayer seeks and reports position via currentTime, not live offset', () => {
    const playerPath = path.resolve(
      path.dirname(fileURLToPath(import.meta.url)),
      '../audio/FanPerformancePlayer.tsx',
    );
    const source = readFileSync(playerPath, 'utf8');
    assert.match(source, /status\.currentTime/);
    assert.match(source, /player\.seekTo/);
    assert.match(source, /player\.play\(\)/);
    assert.match(source, /player\.pause\(\)/);
    assert.equal(source.includes('currentOffsetFromLive'), false);
    assert.equal(source.includes('currentDate'), false);
  });
});
