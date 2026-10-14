/** Player-only UIKit orientation boundary; no global portrait unlock. */
const { withAppDelegate, withDangerousMod } = require('expo/config-plugins');
const fs = require('node:fs');
const path = require('node:path');
const tag = 'QueenZone scoped video fullscreen (#1958)';
function once(source, before, after) {
  if (source.split(before).length !== 2) throw new Error('Forum fullscreen native template changed: ' + before.slice(0, 50));
  return source.replace(before, after);
}
function patchNative(source) {
  if (source.includes(tag)) return source;
  source = once(source, '#import "RNCWebViewImpl.h"', '#import "RNCWebViewImpl.h"\n#if TARGET_OS_IOS\n#import <AVKit/AVKit.h>\n#endif');
  source = once(source, 'static NSTimer *keyboardTimer;', `#if TARGET_OS_IOS
// ${tag}: public controller ancestry, never private UIKit class names.
static BOOL qzHasPresentedVideo(UIViewController *controller, NSUInteger depth) {
  if (!controller || depth > 16) return NO;
  if ([controller isKindOfClass:[AVPlayerViewController class]]) return YES;
  for (UIViewController *child in controller.childViewControllers) {
    if (qzHasPresentedVideo(child, depth + 1)) return YES;
  }
  return qzHasPresentedVideo(controller.presentedViewController, depth + 1);
}
#endif
static NSTimer *keyboardTimer;`);
  source = source.replace('  [_webView evaluateJavaScript:@"window.qzNativeFullscreen && window.qzNativeFullscreen(true);" completionHandler:nil];', '  [self qzScheduleFullscreenCheck];');
  source = source.replace('  [_webView evaluateJavaScript:@"window.qzNativeFullscreen && window.qzNativeFullscreen(false);" completionHandler:nil];', '  [self qzScheduleFullscreenCheck];');
  source = once(source, '#if TARGET_OS_IOS\n-(void)showFullScreenVideoStatusBars', `#if TARGET_OS_IOS
- (void)qzReportFullscreen:(BOOL)active {
  NSNumber *previous = objc_getAssociatedObject(self, @selector(qzReportFullscreen:));
  if (previous.boolValue == active) return;
  objc_setAssociatedObject(self, @selector(qzReportFullscreen:), @(active), OBJC_ASSOCIATION_RETAIN_NONATOMIC);
  NSHashTable *windows = objc_getAssociatedObject(self, @selector(qzScheduleFullscreenCheck));
  UIWindow *window = windows.allObjects.firstObject;
  if (window) [[NSNotificationCenter defaultCenter] postNotificationName:@"QZForumVideoFullscreenChanged" object:window userInfo:@{@"player":self,@"active":@(active)}];
  [_webView evaluateJavaScript:active ? @"window.qzNativeFullscreen && window.qzNativeFullscreen(true);" : @"window.qzNativeFullscreen && window.qzNativeFullscreen(false);" completionHandler:nil];
}
- (void)qzScheduleFullscreenCheck {
  if (![_source[@"baseUrl"] isEqualToString:@"https://org.queenzone.mobile/"] || !_webView) return;
  NSHashTable *windows = objc_getAssociatedObject(self, @selector(qzScheduleFullscreenCheck));
  if (!windows && self.window) {
    windows = [NSHashTable weakObjectsHashTable]; [windows addObject:self.window];
    objc_setAssociatedObject(self, @selector(qzScheduleFullscreenCheck), windows, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
  }
  __weak RNCWebViewImpl *weakSelf = self;
  // Visibility fires before UIKit attaches the presented video controller.
  // Two bounded checks also cover the end of its presentation transition.
  for (NSNumber *delay in @[@0.1, @0.5]) dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(delay.doubleValue * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
    RNCWebViewImpl *strongSelf = weakSelf; if (!strongSelf || !strongSelf->_webView) return;
    UIWindow *original = windows.allObjects.firstObject; BOOL active = NO;
    for (UIWindow *candidate in original.windowScene.windows) {
      if (candidate != original && !candidate.hidden && candidate.rootViewController.presentedViewController
          && qzHasPresentedVideo(candidate.rootViewController, 0)) { active = YES; break; }
    }
    [strongSelf qzReportFullscreen:active];
  });
}
-(void)showFullScreenVideoStatusBars`);
  source = once(source, '  if (_webView) {\n    [_webView.configuration.userContentController removeScriptMessageHandlerForName:HistoryShimName];', `#if TARGET_OS_IOS
  [self qzReportFullscreen:NO];
#endif
  if (_webView) {
    [_webView.configuration.userContentController removeScriptMessageHandlerForName:HistoryShimName];`);
  return source;
}
const sceneMethods = `  // ${tag}
  private var qzVideoObserver: NSObjectProtocol?
  private let qzFullscreenPlayers = NSHashTable<AnyObject>.weakObjects()
  private var qzPreviousOrientation: UIInterfaceOrientation?
  private var qzBaselineOrientations: UIInterfaceOrientationMask {
    let key = UIDevice.current.userInterfaceIdiom == .pad ? "UISupportedInterfaceOrientations~ipad" : "UISupportedInterfaceOrientations"
    let names = (Bundle.main.object(forInfoDictionaryKey: key) ?? Bundle.main.object(forInfoDictionaryKey: "UISupportedInterfaceOrientations")) as? [String] ?? []
    var mask: UIInterfaceOrientationMask = []
    for name in names {
      switch name {
      case "UIInterfaceOrientationPortrait": mask.insert(.portrait)
      case "UIInterfaceOrientationPortraitUpsideDown": mask.insert(.portraitUpsideDown)
      case "UIInterfaceOrientationLandscapeLeft": mask.insert(.landscapeLeft)
      case "UIInterfaceOrientationLandscapeRight": mask.insert(.landscapeRight)
      default: break
      }
    }
    return mask.isEmpty ? .portrait : mask
  }
  @available(iOS 27.0, *)
  func supportedInterfaceOrientations(for windowScene: UIWindowScene) -> UIInterfaceOrientationMask {
    return qzSupportedOrientations(in: windowScene)
  }
  func qzSupportedOrientations(in windowScene: UIWindowScene) -> UIInterfaceOrientationMask {
    return qzFullscreenPlayers.allObjects.isEmpty ? qzBaselineOrientations : .allButUpsideDown
  }
  private func qzObserveVideo(in window: UIWindow) {
    qzVideoObserver = NotificationCenter.default.addObserver(forName: NSNotification.Name("QZForumVideoFullscreenChanged"), object: window, queue: .main) { [weak self, weak window] note in
      guard let self, let scene = window?.windowScene, let player = note.userInfo?["player"] as AnyObject?, let active = note.userInfo?["active"] as? Bool else { return }
      let wasFullscreen = !self.qzFullscreenPlayers.allObjects.isEmpty
      if active {
        if !wasFullscreen { self.qzPreviousOrientation = scene.interfaceOrientation }
        self.qzFullscreenPlayers.add(player)
      } else { self.qzFullscreenPlayers.remove(player) }
      let fullscreen = !self.qzFullscreenPlayers.allObjects.isEmpty
      if wasFullscreen == fullscreen { return }
      if #available(iOS 16.0, *) {
      for nativeWindow in scene.windows {
        nativeWindow.rootViewController?.setNeedsUpdateOfSupportedInterfaceOrientations()
        nativeWindow.rootViewController?.presentedViewController?.setNeedsUpdateOfSupportedInterfaceOrientations()
      }
      var requested: UIInterfaceOrientationMask = .allButUpsideDown
      if !fullscreen {
        switch self.qzPreviousOrientation {
        case .landscapeLeft: requested = .landscapeLeft
        case .landscapeRight: requested = .landscapeRight
        case .portraitUpsideDown: requested = .portraitUpsideDown
        default: requested = .portrait
        }
        requested.formIntersection(self.qzBaselineOrientations)
        if requested.isEmpty { requested = self.qzBaselineOrientations }
        self.qzPreviousOrientation = nil
      }
      scene.requestGeometryUpdate(.iOS(interfaceOrientations: requested)) { _ in /* UIKit keeps its supported orientation if presentation has already ended. */ }
      } else { UIViewController.attemptRotationToDeviceOrientation() }
    }
  }
  deinit { if let observer = qzVideoObserver { NotificationCenter.default.removeObserver(observer) } }
`;
function patchScene(source) {
  if (source.includes(tag)) return source;
  source = source.replace(/\n {2}\/\/ @generated begin qz-forum-app-orientation[\s\S]*?\/\/ @generated end qz-forum-app-orientation\n/, '\n');
  source = once(source, '\n  // Linking API', `
  // @generated begin qz-forum-app-orientation
  public override func application(_ application: UIApplication, supportedInterfaceOrientationsFor window: UIWindow?) -> UIInterfaceOrientationMask {
    if let scene = window?.windowScene, let delegate = scene.delegate as? SceneDelegate {
      return delegate.qzSupportedOrientations(in: scene)
    }
    return super.application(application, supportedInterfaceOrientationsFor: window)
  }
  // @generated end qz-forum-app-orientation
  // Linking API`);
  source = once(source, 'class SceneDelegate: UIResponder, UIWindowSceneDelegate {' , 'class SceneDelegate: UIResponder, UIWindowSceneDelegate {\n' + sceneMethods);
  return once(source, '    window = nextWindow\n', '    window = nextWindow\n    qzObserveVideo(in: nextWindow)\n');
}
function plugin(config) {
  config = withAppDelegate(config, mod => {
    if (mod.modResults.language !== 'swift') throw new Error('Forum fullscreen requires the Swift scene delegate.');
    mod.modResults.contents = patchScene(mod.modResults.contents); return mod;
  });
  return withDangerousMod(config, ['ios', mod => {
    const file = path.join(mod.modRequest.projectRoot, 'node_modules/react-native-webview/apple/RNCWebViewImpl.m');
    fs.writeFileSync(file, patchNative(fs.readFileSync(file, 'utf8'))); return mod;
  }]);
}
module.exports = plugin;
module.exports.patchNative = patchNative;
module.exports.patchScene = patchScene;
