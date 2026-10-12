/** CNG boundary for WebView 13.16.1 (#1958).
 * Upstream has no native fullscreen JS event and Android's download listener
 * bypasses onFileDownload. Fail closed on template drift. Remove when upstream
 * exposes these controls. No generated native files are committed.
 */
const fs = require('node:fs');
const path = require('node:path');
const { withDangerousMod } = require('expo/config-plugins');
const marker = 'QueenZone player boundary (#1958)';
function replaceOnce(source, old, next) {
  if (source.split(old).length !== 2) throw new Error('WebView native template changed: ' + old.slice(0, 60));
  return source.replace(old, next);
}
function patchAndroid(source) {
  if (source.includes(marker)) return source;
  const start = source.indexOf('        webView.setDownloadListener(DownloadListener {');
  const end = source.indexOf('        return RNCWebViewWrapper(context, webView)', start);
  if (start < 0 || end < 0 || !source.slice(start, end).trimEnd().endsWith('})')) throw new Error('WebView download listener template changed');
  source = source.slice(0, start) + `        // ${marker}: never offer downloads.
        webView.setDownloadListener(null)
` + source.slice(end);
  source = replaceOnce(source, '                        mVideoView = view', `                        mWebView.evaluateJavascript("window.qzNativeFullscreen && window.qzNativeFullscreen(true);", null)
                        mVideoView = view`);
  return replaceOnce(source, '                        rootView.removeView(mVideoView)', `                        mWebView.evaluateJavascript("window.qzNativeFullscreen && window.qzNativeFullscreen(false);", null)
                        rootView.removeView(mVideoView)`);
}
function patchIos(source) {
  if (source.includes(marker)) return source;
  source = replaceOnce(source, '-(void)showFullScreenVideoStatusBars\n{', `-(void)showFullScreenVideoStatusBars
{
  // ${marker}: WK native video fullscreen is not a DOM fullscreen event.
  [_webView evaluateJavaScript:@"window.qzNativeFullscreen && window.qzNativeFullscreen(true);" completionHandler:nil];`);
  return replaceOnce(source, '-(void)hideFullScreenVideoStatusBars\n{', `-(void)hideFullScreenVideoStatusBars
{
  [_webView evaluateJavaScript:@"window.qzNativeFullscreen && window.qzNativeFullscreen(false);" completionHandler:nil];`);
}
function patchAndroidNavigation(source) {
  if (source.includes('QueenZone navigation boundary (#1958)')) return source;
  source = replaceOnce(source, 'public boolean shouldOverrideUrlLoading(WebView view, String url) {', String.raw`public boolean shouldOverrideUrlLoading(WebView view, String url) {
        return qzShouldOverride(view, url, true, false);
    }

    // QueenZone navigation boundary (#1958): retain OS frame/gesture identity,
    // and fail closed if JavaScript cannot answer the navigation callback.
    private boolean qzShouldOverride(WebView view, String url, boolean isTopFrame, boolean hasGesture) {
        final boolean fixedDocument = url.equals("about:blank") || url.equals("https://org.queenzone.mobile/");
        final boolean approvedEmbed = !isTopFrame && url.matches("https://www\\.youtube\\.com/embed/[A-Za-z0-9_-]{11}(?:\\?.*)?");
        final boolean blockedNavigation = !fixedDocument && !approvedEmbed;
        if (fixedDocument || approvedEmbed) return false;`);
  source = replaceOnce(source, 'event.putDouble("lockIdentifier", lockIdentifier);', `event.putDouble("lockIdentifier", lockIdentifier);
            event.putBoolean("isTopFrame", isTopFrame);
            event.putString("navigationType", hasGesture ? "click" : "other");`);
  source = replaceOnce(source, 'int reactTag = RNCWebViewWrapper.getReactTagFromWebView(view);\n            UIManagerHelper', `int reactTag = RNCWebViewWrapper.getReactTagFromWebView(view);
            final WritableMap qzNavigationEvent = createWebViewEvent(view, url);
            qzNavigationEvent.putBoolean("isTopFrame", isTopFrame);
            qzNavigationEvent.putString("navigationType", hasGesture ? "click" : "other");
            UIManagerHelper`);
  source = replaceOnce(source, 'createWebViewEvent(view, url)));', 'qzNavigationEvent));');
  source = source.replace('defaulting to allow loading.', 'applying fixed-player navigation policy.');
  // These returns are only in the synchronous navigation helper.
  const start = source.indexOf('    private boolean qzShouldOverride(');
  const end = source.indexOf('    @TargetApi(Build.VERSION_CODES.N)', start);
  if (start < 0 || end < 0) throw new Error('WebView navigation helper template changed');
  const helper = source.slice(start, end).replaceAll('return false;', 'return blockedNavigation;').replace('return shouldOverride;', 'return shouldOverride || blockedNavigation;');
  source = source.slice(0, start) + helper + source.slice(end);
  return replaceOnce(source, 'return this.shouldOverrideUrlLoading(view, url);', 'return qzShouldOverride(view, url, request.isForMainFrame(), request.hasGesture());');
}
function patchAndroidPopups(source) {
  if (source.includes('QueenZone popup boundary (#1958)')) return source;
  const begin = source.indexOf('    public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {');
  const end = source.indexOf('    @Override\n    public boolean onConsoleMessage(', begin);
  if (begin < 0 || end < 0 || !source.slice(begin, end).includes('new WebView(view.getContext())')) throw new Error('WebView popup template changed');
  return source.slice(0, begin) + `    public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
        // QueenZone popup boundary (#1958): no transient/unmanaged WebView.
        if (isUserGesture && mHasOnOpenWindowEvent) {
            final String url = view.getHitTestResult().getExtra();
            if (url != null) {
                WritableMap event = Arguments.createMap();
                event.putString("targetUrl", url);
                ((RNCWebView) view).dispatchEvent(view,
                    new TopOpenWindowEvent(RNCWebViewWrapper.getReactTagFromWebView(view), event));
            }
        }
        return false;
    }

` + source.slice(end);
}
function patchIosPopups(source) {
  if (source.includes('QueenZone popup boundary (#1958)')) return source;
  if (source.split('_onOpenWindow(event);').length !== 3) throw new Error('WK popup callback template changed');
  return source.replaceAll('_onOpenWindow(event);', `// QueenZone popup boundary (#1958): only an OS-identified link activation.
      if (navigationAction.navigationType == WKNavigationTypeLinkActivated) _onOpenWindow(event);`);
}
function plugin(config) {
  for (const [platform, relative, patch] of [
    ['android', 'android/src/main/java/com/reactnativecommunity/webview/RNCWebViewManagerImpl.kt', patchAndroid],
    ['ios', 'apple/RNCWebViewImpl.m', patchIos],
    ['android', 'android/src/main/java/com/reactnativecommunity/webview/RNCWebViewClient.java', patchAndroidNavigation],
    ['android', 'android/src/main/java/com/reactnativecommunity/webview/RNCWebChromeClient.java', patchAndroidPopups],
    ['ios', 'apple/RNCWebViewImpl.m', patchIosPopups],
  ]) config = withDangerousMod(config, [platform, mod => {
    const target = path.join(mod.modRequest.projectRoot, 'node_modules/react-native-webview', relative);
    fs.writeFileSync(target, patch(fs.readFileSync(target, 'utf8')));
    return mod;
  }]);
  return config;
}
module.exports = plugin;
module.exports.patchAndroid = patchAndroid;
module.exports.patchIos = patchIos;

module.exports.patchAndroidNavigation = patchAndroidNavigation;

module.exports.patchAndroidPopups = patchAndroidPopups;
module.exports.patchIosPopups = patchIosPopups;
