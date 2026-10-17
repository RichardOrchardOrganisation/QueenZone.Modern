import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
const require = createRequire(import.meta.url);
const { patchAndroid, patchIos, patchAndroidNavigation, patchAndroidPopups, patchIosPopups } = require('../../plugins/withForumWebViewBoundary.cjs');
describe('CNG forum WebView native boundary', () => {
  it('disables native downloads and reports Android fullscreen idempotently', () => {
    const source = readFileSync(new URL('../../node_modules/react-native-webview/android/src/main/java/com/reactnativecommunity/webview/RNCWebViewManagerImpl.kt', import.meta.url), 'utf8');
    const next = patchAndroid(source);
    assert.match(next, /setDownloadListener\(null\)/); assert.doesNotMatch(next, /module.downloadFile/);
    assert.match(next, /qzNativeFullscreen\(true\)/); assert.match(next, /qzNativeFullscreen\(false\)/);
    assert.equal(patchAndroid(next), next);
  });
  it('reports WK native fullscreen idempotently and fails on changed templates', () => {
    const source = readFileSync(new URL('../../node_modules/react-native-webview/apple/RNCWebViewImpl.m', import.meta.url), 'utf8');
    const next = patchIos(source);
    assert.match(next, /qzNativeFullscreen\(true\)/); assert.match(next, /qzNativeFullscreen\(false\)/);
    assert.equal(patchIos(next), next);
    assert.throws(() => patchIos('upstream changed'));
    assert.throws(() => patchAndroid('upstream changed'));
  });
  it('preserves Android main-frame and gesture identity and fails closed on JS timeout', () => {
    const source = readFileSync(new URL('../../node_modules/react-native-webview/android/src/main/java/com/reactnativecommunity/webview/RNCWebViewClient.java', import.meta.url), 'utf8');
    const next = patchAndroidNavigation(source);
    const overloadAnchor = '    @Override\n    public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {';
    const overloadStart = next.indexOf(overloadAnchor);
    assert.ok(overloadStart > next.indexOf('    private boolean qzShouldOverride('));
    assert.match(next.slice(0, overloadStart), /    }\n\n$/);
    assert.equal(next.slice(overloadStart), source.slice(source.indexOf(overloadAnchor)).replace(
      'return this.shouldOverrideUrlLoading(view, url);',
      'return qzShouldOverride(view, url, request.isForMainFrame(), request.hasGesture());',
    ));
    assert.match(next.slice(overloadStart), /^    @Override\n    public boolean shouldOverrideUrlLoading\(WebView view, WebResourceRequest request\) {\n        final String url = request.getUrl\(\).toString\(\);\n        return qzShouldOverride\(view, url, request.isForMainFrame\(\), request.hasGesture\(\)\);\n    }/);
    assert.throws(() => patchAndroidNavigation(source.replace(overloadAnchor, '')), /WebView navigation helper template changed/);
    assert.match(next, /putBoolean\("isTopFrame", isTopFrame\)/);
    assert.match(next, /return shouldOverride \|\| blockedNavigation/);
    assert.match(next, /return blockedNavigation/);
    assert.equal(patchAndroidNavigation(next), next);
    assert.throws(() => patchAndroidNavigation('upstream changed'));
  });
  it('rejects unmanaged popup WebViews and forwards only native user link activations', () => {
    const android = readFileSync(new URL('../../node_modules/react-native-webview/android/src/main/java/com/reactnativecommunity/webview/RNCWebChromeClient.java', import.meta.url), 'utf8');
    const next = patchAndroidPopups(android);
    assert.doesNotMatch(next, /new WebView\(view.getContext\(\)\)/);
    assert.doesNotMatch(next, /shouldOverrideUrlLoading \(WebView subview/);
    const nested = '    public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {\n        WebView child = new WebView(view.getContext());\n        child.setWebViewClient(new WebViewClient() {\n            @Override\n            public boolean shouldOverrideUrlLoading (WebView subview, String url) { return true; }\n        });\n        return true;\n    }\n    @Override\n    public boolean onConsoleMessage(ConsoleMessage message) { return true; }';
    const patchedNested = patchAndroidPopups(nested);
    assert.doesNotMatch(patchedNested, /WebView subview|setWebViewClient/);
    assert.match(patchedNested, /public boolean onConsoleMessage/);
    assert.match(next, /isUserGesture && mHasOnOpenWindowEvent/);
    assert.equal(patchAndroidPopups(next), next);
    const ios = readFileSync(new URL('../../node_modules/react-native-webview/apple/RNCWebViewImpl.m', import.meta.url), 'utf8');
    const wk = patchIosPopups(ios);
    assert.match(wk, /navigationAction.navigationType == WKNavigationTypeLinkActivated/);
    assert.equal(patchIosPopups(wk), wk);
    assert.throws(() => patchAndroidPopups('changed'));
    assert.throws(() => patchIosPopups('changed'));
  });
  it('fingerprints the native runtime instead of reusing a JS-only version', () => {
    const config = readFileSync(new URL('../../app.config.ts', import.meta.url), 'utf8');
    assert.match(config, /runtimeVersion: \{ policy: 'fingerprint' \}/);
    assert.match(config, /withForumWebViewBoundary.cjs/);
  });
});

describe('scoped iOS forum fullscreen generation', () => {
  const { patchNative, patchScene } = require('../../plugins/withForumFullscreenOrientation.cjs');
  it('uses public video ancestry, weak window ownership and explicit teardown instead of trusting overlay visibility', () => {
    const original = readFileSync(new URL('../../node_modules/react-native-webview/apple/RNCWebViewImpl.m', import.meta.url), 'utf8');
    const next = patchNative(patchIos(original));
    assert.match(next, /isKindOfClass:\[AVPlayerViewController class\]/);
    assert.match(next, /candidate != original && !candidate.hidden/);
    assert.match(next, /NSHashTable weakObjectsHashTable/);
    assert.match(next, /\[self qzReportFullscreen:NO\]/);
    assert.doesNotMatch(next.slice(next.indexOf("static BOOL qzHasPresentedVideo"), next.indexOf("static NSTimer *keyboardTimer")), /NSClassFromString|AVFullScreenViewController|setValue:.*orientation/);
    assert.equal(patchNative(next), next);
    assert.throws(() => patchNative('changed upstream'));
  });
  it('preserves normal masks and prior orientation, scopes observation to its window and rejects scene drift', () => {
    const fixture = 'class AppDelegate {\n  // Linking API\n}\nclass SceneDelegate: UIResponder, UIWindowSceneDelegate {\n  var window: UIWindow?\n  func connect() {\n    window = nextWindow\n  }\n}';
    const next = patchScene(fixture);
    assert.match(next, /qzFullscreenPlayers.allObjects.isEmpty \? qzBaselineOrientations : .allButUpsideDown/);
    assert.match(next, /UISupportedInterfaceOrientations~ipad/);
    assert.match(next, /object: window, queue: .main/);
    assert.match(next, /qzPreviousOrientation = scene.interfaceOrientation/);
    assert.match(next, /requested.formIntersection\(self.qzBaselineOrientations\)/);
    assert.equal(patchScene(next), next);
    const regeneratedScene = next.slice(0, next.indexOf('class SceneDelegate:')) + fixture.slice(fixture.indexOf('class SceneDelegate:'));
    assert.equal((patchScene(regeneratedScene).match(/public override func application/g) ?? []).length, 1, 'Regenerating the scene does not duplicate the legacy AppDelegate orientation override');
    assert.throws(() => patchScene('changed scene template'));
  });
});
