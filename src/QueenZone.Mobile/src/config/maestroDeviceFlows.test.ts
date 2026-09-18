import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

const maestroDir = new URL('../../maestro/', import.meta.url);
const scriptsDir = new URL('../../../../scripts/', import.meta.url);
const workflowsDir = new URL('../../../../.github/workflows/', import.meta.url);

function readMaestro(name: string): string {
  return readFileSync(new URL(name, maestroDir), 'utf8');
}

function readRepo(relFromWorkflowsOrScripts: string, base: URL): string {
  return readFileSync(new URL(relFromWorkflowsOrScripts, base), 'utf8');
}

describe('Maestro device flows (#1281)', () => {
  it('always reselects ForumIndex so Home to Forum cannot restore leftover Category', () => {
    const nav = readFileSync(new URL('../navigation/RootNavigator.tsx', import.meta.url), 'utf8');
    assert.match(nav, /reselectRoot\('ForumTab', 'ForumIndex', \{ always: true \}\)/);
  });

  it('does not add journeys to smoke.yaml', () => {
    const smoke = readMaestro('smoke.yaml');
    assert.match(smoke, /flows\/01-launch\.yaml/);
    assert.match(smoke, /flows\/09-authenticated\.yaml/);
    assert.doesNotMatch(smoke, /10-forum-attach|11-news-discussion|12-masthead-unread/);
  });

  it('keeps the P0 release suite separate and independently resets each write journey', () => {
    const release = readMaestro('release.yaml');
    assert.match(release, /flows\/13-messages-lifecycle\.yaml/);
    assert.match(release, /flows\/14-forum-create-reply\.yaml/);
    assert.match(release, /flows\/15-news-suggestion\.yaml/);

    for (const flow of [
      'flows/13-messages-lifecycle.yaml',
      'flows/14-forum-create-reply.yaml',
      'flows/15-news-suggestion.yaml',
    ]) {
      const content = readMaestro(flow);
      assert.match(content, /launchApp:[\s\S]*clearState: true[\s\S]*clearKeychain: true/);
      assert.match(content, /id: home-screen[\s\S]*runFlow: open-smoke-auth\.yaml/);
      assert.doesNotMatch(content, /retry:/);
    }
  });

  it('covers message, forum, and submission outcomes through stable selectors', () => {
    const messages = readMaestro('flows/13-messages-lifecycle.yaml');
    assert.match(messages, /id: compose-message-recipient/);
    assert.match(messages, /id: conversation-reply-submit/);
    assert.match(messages, /id: conversation-archive/);
    assert.match(messages, /text: '\^Unarchive\$'/);

    const forum = readMaestro('flows/14-forum-create-reply.yaml');
    assert.match(forum, /id: forum-composer-title/);
    assert.match(forum, /id: forum-composer-submit/);
    assert.match(forum, /text: Release forum reply/);

    const suggestion = readMaestro('flows/15-news-suggestion.yaml');
    assert.match(suggestion, /id: suggest-news-success/);
    assert.match(suggestion, /id: suggest-news-view-submissions/);
    assert.match(suggestion, /id: my-submissions-tab-news/);
    assert.match(suggestion, /visible: Release news suggestion/);
  });

  it('keeps the #1247 journeys shape and waits for chrome before smoke-auth', () => {
    const attach = readMaestro('flows/10-forum-attach.yaml');
    assert.match(attach, /launchApp:/);
    assert.match(attach, /id: home-screen/);
    assert.match(attach, /runFlow: open-smoke-auth\.yaml/);
    const homeIdx = attach.indexOf('id: home-screen');
    const authIdx = attach.indexOf('runFlow: open-smoke-auth.yaml');
    assert.ok(homeIdx >= 0 && authIdx > homeIdx);
    assert.match(attach, /id: tab-forum/);
  });

  it('uses the composer test ID and dismisses the keyboard before attaching a file', () => {
    const attach = readMaestro('flows/10-forum-attach.yaml');
    assert.match(
      attach,
      /id: forum-composer-body[\s\S]*inputText: Journey attach reply[\s\S]*hideKeyboard[\s\S]*id: forum-composer-attach-files/,
    );
    assert.match(attach, /id: forum-composer-attachment/);
    assert.match(attach, /id: forum-thread-attachment/);
    assert.doesNotMatch(attach, /tapOn:\s+text: Write a reply/);
    assert.doesNotMatch(attach, /visible: attach\.txt|text: attach\.txt|tapOn: attach\.txt/);
  });

  it('recovers one transient thread read while keeping forum-thread-screen mandatory', () => {
    const attach = readMaestro('flows/10-forum-attach.yaml');
    assert.match(
      attach,
      /id: forum-thread-\$\{ATTACH_TOPIC_ID\}[\s\S]*retry:[\s\S]*maxRetries: 1[\s\S]*text: '\^Try again\$'[\s\S]*id: forum-thread-screen[\s\S]*assertVisible: Journey attach topic/,
    );
    assert.equal(attach.match(/maxRetries: 1/g)?.length, 1);
  });

  it('leaves the archive search story before the forum flow switches tabs', () => {
    const search = readMaestro('flows/06-archive-search.yaml');
    assert.match(
      search,
      /id: news-story-screen[\s\S]*id: news-story-back[\s\S]*id: news-screen[\s\S]*id: tab-home[\s\S]*id: search-screen[\s\S]*platform: Android[\s\S]*- back[\s\S]*platform: iOS[\s\S]*text: '\^Home\$'[\s\S]*id: home-screen/,
    );
  });

  it('resets the forum stack after its thread journey', () => {
    const forum = readMaestro('flows/07-forum.yaml');
    assert.match(
      forum,
      /id: forum-thread-screen[\s\S]*id: forum-thread-back[\s\S]*id: forum-category-screen[\s\S]*id: tab-forum[\s\S]*id: forum-screen/,
    );
  });

  it('returns each on-demand journey to a tab root', () => {
    const attach = readMaestro('flows/10-forum-attach.yaml');
    const discussion = readMaestro('flows/11-news-discussion.yaml');

    assert.match(
      attach,
      /id: forum-thread-attachment-opened[\s\S]*id: forum-thread-back[\s\S]*id: forum-category-screen[\s\S]*id: tab-forum[\s\S]*id: forum-screen/,
    );
    assert.match(
      discussion,
      /id: forum-thread-back[\s\S]*id: forum-screen/,
    );
  });

  it('scrolls the discussion reply action into view on compact emulators', () => {
    const discussion = readMaestro('flows/11-news-discussion.yaml');
    assert.match(
      discussion,
      /id: forum-thread-screen[\s\S]*scrollUntilVisible:[\s\S]*id: forum-thread-reply[\s\S]*assertVisible:[\s\S]*id: forum-thread-reply/,
    );
  });

  it('waits out profile-restoring before asserting signed-out profile', () => {
    const profile = readMaestro('flows/08-profile-signed-out.yaml');
    assert.match(
      profile,
      /id: home-profile[\s\S]*notVisible:[\s\S]*id: profile-restoring[\s\S]*id: profile-signed-out/,
    );
  });

  it('types the attach reply through a composer testID and reveals inject after hideKeyboard', () => {
    const attach = readMaestro('flows/10-forum-attach.yaml');
    assert.match(attach, /id: forum-composer-body/);
    assert.doesNotMatch(attach, /text: Write a reply/);
    assert.match(
      attach,
      /id: forum-composer-body[\s\S]*hideKeyboard[\s\S]*id: forum-composer-attach-files[\s\S]*forum-composer-attach-inject/,
    );
  });

  it('dismisses the iOS Open-in-QueenZone confirm after smoke-auth and attach', () => {
    const openAuth = readMaestro('flows/open-smoke-auth.yaml');
    const accept = readMaestro('flows/accept-ios-open-link.yaml');
    assert.match(openAuth, /openLink: \$\{SMOKE_AUTH_URL\}/);
    assert.match(openAuth, /accept-ios-open-link\.yaml/);
    assert.match(accept, /platform: iOS/);
    assert.match(
      accept,
      /extendedWaitUntil:[\s\S]*visible:[\s\S]*text: '\^Open\$'[\s\S]*timeout: 15000[\s\S]*optional: true/,
    );
    assert.match(accept, /tapOn:[\s\S]*text: '\^Open\$'[\s\S]*optional: true/);
    assert.match(accept, /retryTapIfNoChange: true/);
    assert.doesNotMatch(accept, /Open in \.\*QueenZone/);
    assert.match(accept, /\^Open\$/);

    const authenticated = readMaestro('flows/09-authenticated.yaml');
    assert.equal(authenticated.match(/open-smoke-auth\.yaml/g)?.length, 1);
    assert.doesNotMatch(authenticated, /openLink: \$\{SMOKE_AUTH_URL\}/);
    assert.match(
      openAuth,
      /accept-ios-open-link\.yaml[\s\S]*platform: iOS[\s\S]*notVisible:[\s\S]*id: home-messages[\s\S]*openLink: \$\{SMOKE_AUTH_URL\}[\s\S]*accept-ios-open-link\.yaml[\s\S]*visible:[\s\S]*id: home-messages/,
    );
    assert.match(authenticated, /id: home-profile[\s\S]*id: profile-signed-in/);
    assert.match(authenticated, /id: profile-messages/);
    assert.match(readMaestro('flows/12-masthead-unread.yaml'), /open-smoke-auth\.yaml/);
    assert.match(readMaestro('flows/10-forum-attach.yaml'), /accept-ios-open-link\.yaml/);
  });

  it('waits for the delayed iOS Open confirm before asserting home-messages (#1455)', () => {
    const accept = readMaestro('flows/accept-ios-open-link.yaml');
    const waitIdx = accept.indexOf('extendedWaitUntil:');
    const tapIdx = accept.indexOf('tapOn:');
    assert.ok(waitIdx >= 0 && tapIdx > waitIdx);

    const openAuth = readMaestro('flows/open-smoke-auth.yaml');
    assert.match(openAuth, /visible:[\s\S]*id: home-messages/);
    assert.doesNotMatch(openAuth, /#.*id: home-messages/);
    assert.match(readMaestro('journeys.yaml'), /flows\/10-forum-attach\.yaml/);
    assert.match(readMaestro('flows/10-forum-attach.yaml'), /runFlow: open-smoke-auth\.yaml/);
  });

  it('matches the seeded inbox row when iOS merges its accessibility label', () => {
    const authenticated = readRepo('flows/09-authenticated.yaml', maestroDir);
    assert.match(authenticated, /text: '\^Contract Other\.\*'/);
    assert.match(readMaestro('flows/12-masthead-unread.yaml'), /text: '\^Contract Other\.\*'/);
  });

  it('bounds news story recovery to one retry of the read-only error action', () => {
    const newsStory = readRepo('flows/04-news-story.yaml', maestroDir);
    assert.match(newsStory, /retry:\s+maxRetries: 1/);
    assert.match(newsStory, /text: '\^Try again\$'/);
    assert.match(newsStory, /id: news-story-screen/);
  });

  it('locks Set as wallpaper in photo viewer chrome beside Save (#1409)', () => {
    const photos = readMaestro('flows/05-photography.yaml');
    assert.match(
      photos,
      /id: photo-viewer-screen[\s\S]*id: photo-viewer-wallpaper[\s\S]*id: photo-viewer-save[\s\S]*id: photo-viewer-close/,
    );
    assert.doesNotMatch(photos, /id: photo-viewer-wallpaper[\s\S]*tapOn:[\s\S]*photo-viewer-wallpaper/);
  });
});

describe('device-smoke harness (#1281)', () => {
  it('routes suite=release through the harness and both device jobs', () => {
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);
    const workflow = readRepo('mobile-device-smoke.yml', workflowsDir);
    assert.match(script, /smoke\|journeys\|release/);
    assert.match(script, /maestro\/release\.yaml/);
    assert.match(workflow, /- release/);
    assert.equal(workflow.match(/github\.event\.inputs\.suite == 'release'/g)?.length, 2);
    assert.match(workflow, /mobile-android-release:[\s\S]*run-mobile-android-release-self-hosted\.sh/);
    assert.match(workflow, /SUITE="release"/);
    assert.match(workflow, /platform:[\s\S]*- both[\s\S]*- android[\s\S]*- ios/);
    assert.equal(workflow.match(/github\.event\.inputs\.platform == 'android'/g)?.length, 3);
    assert.equal(workflow.match(/github\.event\.inputs\.platform == 'ios'/g)?.length, 2);
  });

  it('releases Gradle memory and isolates hosted journeys from P0 Android runners', () => {
    const workflow = readRepo('mobile-device-smoke.yml', workflowsDir);
    const journeysJob = workflow.slice(
      workflow.indexOf('  mobile-android-journeys:'),
      workflow.indexOf('  mobile-android-release:'),
    );
    const releaseJob = workflow.slice(
      workflow.indexOf('  mobile-android-release:'),
      workflow.indexOf('  mobile-ios-journeys:'),
    );
    assert.equal((workflow.match(/\.\/gradlew --stop/g) ?? []).length, 3);
    assert.equal((journeysJob.match(/api-level: 34/g) ?? []).length, 2);
    assert.equal((journeysJob.match(/-gpu swiftshader_indirect/g) ?? []).length, 2);
    assert.match(journeysJob, /runs-on: ubuntu-latest/);
    assert.match(journeysJob, /android-emulator-runner/);
    assert.match(workflow, /mobile-android-release:[\s\S]*runs-on: \[self-hosted, macOS, ARM64, queenzone\]/);
    assert.match(workflow, /mobile-android-release:[\s\S]*github\.event\.inputs\.suite == 'release'/);
    assert.match(workflow, /mobile-android-release:[\s\S]*Export installed Android SDK/);
    assert.match(workflow, /ANDROID_HOME=\$SDK_ROOT/);
    assert.doesNotMatch(releaseJob, /actions\/cache/);
    assert.doesNotMatch(releaseJob, /run-mobile-android-smoke-self-hosted/);
  });

  it('runs Android smoke on the Mac self-hosted runner with an isolated AVD', () => {
    const workflow = readRepo('mobile-device-smoke.yml', workflowsDir);
    const runner = readRepo('run-mobile-android-smoke-self-hosted.sh', scriptsDir);
    const release = readRepo('run-mobile-android-release-self-hosted.sh', scriptsDir);
    const smokeJob = workflow.slice(
      workflow.indexOf('  mobile-android-smoke:'),
      workflow.indexOf('  mobile-ios-smoke:'),
    );
    assert.match(smokeJob, /runs-on: \[self-hosted, macOS, ARM64, queenzone\]/);
    assert.match(smokeJob, /run-mobile-android-smoke-self-hosted\.sh/);
    assert.match(smokeJob, /Export installed Android SDK/);
    assert.match(smokeJob, /ANDROID_SMOKE_AVD: QueenZone_CI_Smoke_API_36\b/);
    assert.match(smokeJob, /ANDROID_SMOKE_AVD: QueenZone_CI_Smoke_API_36_retry/);
    assert.match(smokeJob, /ANDROID_SMOKE_EMULATOR_PORT: "5558"/);
    assert.match(smokeJob, /ANDROID_SMOKE_FORCE_AVD: "1"/);
    assert.match(smokeJob, /Retry Android device smoke after transport death/);
    assert.equal((smokeJob.match(/Retry Android device smoke after transport death/g) ?? []).length, 1);
    assert.doesNotMatch(smokeJob, /android-emulator-runner/);
    assert.doesNotMatch(smokeJob, /ubuntu-latest/);
    assert.doesNotMatch(smokeJob, /Enable KVM/);
    assert.doesNotMatch(smokeJob, /ANDROID_RELEASE_AVD/);
    assert.doesNotMatch(smokeJob, /ANDROID_RELEASE_EMULATOR_PORT/);
    assert.match(runner, /ANDROID_SMOKE_AVD:-QueenZone_CI_Smoke_API_36/);
    assert.match(runner, /ANDROID_SMOKE_EMULATOR_PORT:-5558/);
    assert.match(runner, /system-images;android-36;google_apis;arm64-v8a/);
    assert.match(runner, /Another Android emulator is active/);
    assert.match(runner, /-gpu host/);
    assert.match(runner, /MAESTRO_TARGET_DEVICE="\$serial"/);
    assert.match(runner, /--suite smoke/);
    assert.doesNotMatch(runner, /ANDROID_RELEASE_/);
    assert.doesNotMatch(runner, /ANDROID_SMOKE_AVD:-QueenZone_CI_API_36/);
    assert.doesNotMatch(runner, /ANDROID_SMOKE_EMULATOR_PORT:-5556/);
    assert.doesNotMatch(runner, /x86_64/);
    assert.match(release, /ANDROID_RELEASE_AVD:-QueenZone_CI_API_36/);
    assert.match(release, /ANDROID_RELEASE_EMULATOR_PORT:-5556/);
    assert.doesNotMatch(release, /ANDROID_SMOKE_/);
    assert.doesNotMatch(release, /QueenZone_CI_Smoke_API_36/);
    assert.doesNotMatch(release, /ANDROID_RELEASE_EMULATOR_PORT:-5558/);
  });

  it('boots and targets a dedicated hardware-rendered Android release emulator', () => {
    const runner = readRepo('run-mobile-android-release-self-hosted.sh', scriptsDir);
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);
    assert.match(runner, /ANDROID_RELEASE_AVD:-QueenZone_CI_API_36/);
    assert.match(runner, /ANDROID_RELEASE_EMULATOR_PORT:-5556/);
    assert.match(runner, /Another Android emulator is active/);
    assert.match(runner, /rm -rf "\$results_dir"/);
    assert.match(runner, /> "\$results_dir\/harness\.log"/);
    assert.match(runner, /avdmanager.*create avd/);
    assert.match(runner, /-gpu host/);
    assert.match(runner, /MAESTRO_TARGET_DEVICE="\$serial"/);
    assert.match(runner, /--suite release/);
    assert.match(script, /--device "\$MAESTRO_TARGET_DEVICE"/);
  });

  it('prepends the Maestro install dir before probing PATH', () => {
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);
    assert.match(script, /HOME\}\/\.maestro\/bin/);
    assert.match(script, /Maestro failing flow:/);
    assert.match(script, /timeout 30 adb logcat/);
  });

  it('fails leftover Android collect fast enough to upload artifacts', () => {
    const workflow = readRepo('mobile-device-smoke.yml', workflowsDir);
    assert.match(workflow, /Collect leftover Android smoke logs[\s\S]*timeout-minutes: 2/);
    assert.match(workflow, /Collect leftover Android journeys logs[\s\S]*timeout-minutes: 2/);
    assert.match(workflow, /maestro --version/);
  });

  it('retries only an iOS driver failure that happened before a flow began', () => {
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);
    assert.match(script, /iOS driver not ready in time/);
    assert.match(script, /\[ ! -s "\$results_dir\/junit\.xml" \]/);
    assert.match(script, /simctl shutdown/);
    assert.match(script, /simctl bootstatus/);
    assert.match(script, /debug-driver-startup-first/);
    assert.match(script, /Selector and assertion failures are not retried/);
  });

  it('retries only an Android device transport failure', () => {
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);
    const workflow = readRepo('mobile-device-smoke.yml', workflowsDir);
    assert.match(
      script,
      /DeviceServerDiedException\|Device server died\|device offline/,
    );
    assert.match(script, /DEADLINE_EXCEEDED\|host:transport:/);
    assert.match(script, /grep -ERq[\s\S]*"\$results_dir\/debug"/);
    assert.doesNotMatch(
      script,
      /grep -Eq 'DeviceServerDiedException\|Device server died\|device offline' "\$results_dir\/junit\.xml"/,
    );
    assert.match(script, /android-transport-death/);
    assert.match(script, /android_latest_attempt_transport_died/);
    assert.match(script, /write_android_transport_marker "adb-recover-timeout"/);
    assert.match(script, /write_android_transport_marker "emulator-gone"/);
    assert.match(script, /write_android_transport_marker "retry-still-transport-death"/);
    assert.doesNotMatch(
      script,
      /write_android_transport_marker "maestro-device-server-or-emulator-gone"/,
    );
    assert.match(
      script,
      /In-process Android retry failed on a selector or assertion miss\. Not requesting a fresh emulator/,
    );
    // #1530 self-test writes adb-recover-timeout first (after the recover helper
    // is defined, before any APK install). Runtime recover-timeout is written
    // when ADB recover fails, still before the live-emulator retry install.
    // retry-still-transport-death is the only marker that follows that install.
    const recoverIdx = script.indexOf('adb reconnect offline');
    const selfTestWrite = script.indexOf('write_android_transport_marker "adb-recover-timeout"');
    const runtimeRecoverTimeout = script.lastIndexOf(
      'write_android_transport_marker "adb-recover-timeout"',
    );
    const retryInstallIdx = script.lastIndexOf('adb install -r "$apk"');
    const retryStillWrite = script.indexOf('write_android_transport_marker "retry-still-transport-death"');
    assert.ok(recoverIdx >= 0 && selfTestWrite > recoverIdx);
    assert.ok(runtimeRecoverTimeout > selfTestWrite && runtimeRecoverTimeout < retryInstallIdx);
    assert.ok(retryInstallIdx >= 0 && retryStillWrite > retryInstallIdx);
    assert.match(script, /Maestro failing cause: Android device transport death/);
    assert.match(script, /Maestro failing cause: selector or assertion miss/);
    assert.match(script, /debug-android-transport-first/);
    assert.match(script, /adb reconnect offline/);
    assert.match(script, /adb install -r "\$apk"/);
    assert.match(script, /Selector and assertion failures are not retried/);
    assert.match(workflow, /Retry Android device smoke after transport death/);
    assert.match(workflow, /Retry Android device journeys after transport death/);
    assert.match(workflow, /android-transport-death/);
    assert.match(workflow, /Not a selector miss/);
    assert.match(
      workflow,
      /cp -a src\/QueenZone.Mobile\/maestro-results\/\. src\/QueenZone.Mobile\/maestro-results-transport-first\/[\s\S]*rm -rf src\/QueenZone.Mobile\/maestro-results/,
    );
    assert.equal(
      (workflow.match(/rm -rf src\/QueenZone\.Mobile\/maestro-results\r?\n/g) ?? []).length,
      2,
    );
    assert.match(script, /rm -f "\$results_dir\/android-transport-death"/);
    const clearMarker = script.indexOf('rm -f "$results_dir/android-transport-death"');
    assert.ok(clearMarker >= 0 && clearMarker < script.indexOf('run_maestro_once()'));
    assert.doesNotMatch(workflow, /Gilfoyle|Glory11/);

    const pattern = /android_transport_re="([^"]+)"/.exec(script)?.[1];
    assert.ok(pattern, 'android_transport_re is defined');
    const re = new RegExp(pattern);
    assert.match(
      "maestro.android.DeviceServerDiedException: Device server died during 'viewHierarchy' DEADLINE_EXCEEDED",
      re,
    );
    assert.match(
      "Command failed (host:transport:emulator-5554): device 'emulator-5554' not found",
      re,
    );
    assert.doesNotMatch('Assertion is false: id: home-hero is visible', re);
    assert.doesNotMatch('Unknown error', re);
  });

  it('clears journey state before copying the attachment fixture', () => {
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);
    const attach = readMaestro('flows/10-forum-attach.yaml');
    const androidClearIdx = script.indexOf('adb shell pm clear org.queenzone.mobile');
    const iosClearIdx = script.indexOf('simctl uninstall booted org.queenzone.mobile');
    const fixtureIdx = script.lastIndexOf('push_attach_fixture');

    assert.ok(androidClearIdx >= 0 && androidClearIdx < fixtureIdx);
    assert.ok(iosClearIdx >= 0 && iosClearIdx < fixtureIdx);
    assert.doesNotMatch(script, /adb push/);
    assert.match(script, /file:\/\/\/data\/user\/0\/org\.queenzone\.mobile\/cache\/attach\.txt/);
    assert.doesNotMatch(attach, /clearState: true/);
    assert.match(attach, /clearKeychain: true/);
  });
});

describe('device-smoke Release embed (#1322)', () => {
  it('builds and installs Release-embedded binaries, never Debug/dev-client', () => {
    const workflow = readRepo('mobile-device-smoke.yml', workflowsDir);
    const script = readRepo('run-mobile-device-smoke.sh', scriptsDir);

    assert.match(workflow, /assembleRelease/);
    assert.match(workflow, /apk\/release\/app-release\.apk/);
    assert.match(workflow, /-configuration Release/);
    assert.match(workflow, /Products\/Release-iphonesimulator/);
    assert.match(workflow, /QUEENZONE_MOBILE_SMOKE_EMBED/);
    assert.match(workflow, /EXPO_PUBLIC_SMOKE_EMBED/);
    assert.match(workflow, /SENTRY_DISABLE_AUTO_UPLOAD/);
    assert.doesNotMatch(workflow, /\.\/gradlew assembleDebug/);
    assert.doesNotMatch(workflow, /apk\/debug\/app-debug\.apk/);
    assert.doesNotMatch(workflow, /-configuration Debug/);
    assert.doesNotMatch(workflow, /Products\/Debug-iphonesimulator/);
    assert.doesNotMatch(workflow, /npx expo start|expo start --|metro start|packager start/i);

    assert.match(script, /assembleRelease/);
    assert.match(script, /android_release_apk/);
    assert.match(script, /-configuration Release/);
    assert.match(script, /Release-iphonesimulator/);
    assert.match(script, /QUEENZONE_MOBILE_SMOKE_EMBED=1/);
    assert.match(script, /EXPO_PUBLIC_SMOKE_EMBED=1/);
    assert.doesNotMatch(script, /\.\/gradlew assembleDebug/);
    assert.doesNotMatch(script, /apk\/debug\/app-debug\.apk/);
    assert.doesNotMatch(script, /-configuration Debug/);
    assert.doesNotMatch(script, /Products\/Debug-iphonesimulator/);
    assert.doesNotMatch(script, /npx expo start|expo start --|metro start|packager start/i);
  });

  it('documents Release-embedded device smoke in the mobile README', () => {
    const readme = readFileSync(new URL('../../README.md', import.meta.url), 'utf8');
    assert.match(readme, /Release-embedded/);
    assert.match(readme, /assembleRelease/);
    assert.match(readme, /never install `app-debug\.apk`/);
  });

  it('keeps home-screen as the launch assertion', () => {
    const launch = readMaestro('flows/01-launch.yaml');
    assert.match(launch, /id: home-screen/);
    assert.match(readMaestro('smoke.yaml'), /flows\/01-launch\.yaml/);
    assert.match(readMaestro('flows/10-forum-attach.yaml'), /id: home-screen/);
    assert.match(readMaestro('journeys.yaml'), /flows\/10-forum-attach\.yaml/);
  });

  it('asserts the reserved env-banner without replacing home-screen or home-profile', () => {
    const launch = readMaestro('flows/01-launch.yaml');
    assert.match(launch, /id: home-screen/);
    assert.match(launch, /id: home-hero/);
    assert.match(launch, /id: env-banner/);
    const homeIdx = launch.indexOf('id: home-screen');
    const bannerIdx = launch.indexOf('id: env-banner');
    assert.ok(homeIdx >= 0 && bannerIdx > homeIdx);

    const profile = readMaestro('flows/08-profile-signed-out.yaml');
    assert.match(profile, /id: home-profile/);
    assert.doesNotMatch(profile, /id: env-banner[\s\S]*tapOn:/);
  });
});
