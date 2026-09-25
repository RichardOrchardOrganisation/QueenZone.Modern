/**
 * Adopt UIKit's scene lifecycle in the generated Expo iOS project.
 *
 * iOS 27 rejects apps built with the iOS 27 SDK when UIApplicationSceneManifest
 * and a scene delegate are absent. Expo SDK 57 still emits the legacy
 * AppDelegate-owned UIWindow, so patch the CNG output until Expo owns this.
 */
const { createRunOncePlugin, withAppDelegate, withInfoPlist } = require('expo/config-plugins');

const TAG = 'queenzone-ios-scene-lifecycle';

const SCENE_CONFIGURATION_METHOD = `  public func application(
    _ application: UIApplication,
    configurationForConnecting connectingSceneSession: UISceneSession,
    options: UIScene.ConnectionOptions
  ) -> UISceneConfiguration {
    let configuration = UISceneConfiguration(
      name: "Default Configuration",
      sessionRole: connectingSceneSession.role
    )
    configuration.delegateClass = SceneDelegate.self
    return configuration
  }
`;

const SCENE_DELEGATE_CLASS = `// @generated begin ${TAG} - expo prebuild
class SceneDelegate: UIResponder, UIWindowSceneDelegate {
  var window: UIWindow?

  func scene(
    _ scene: UIScene,
    willConnectTo session: UISceneSession,
    options connectionOptions: UIScene.ConnectionOptions
  ) {
    guard let windowScene = scene as? UIWindowScene,
      let appDelegate = UIApplication.shared.delegate as? AppDelegate,
      let factory = appDelegate.reactNativeFactory else {
      return
    }

    let nextWindow = UIWindow(windowScene: windowScene)
    window = nextWindow
    // React Native helpers still consult UIApplicationDelegate.window.
    appDelegate.window = nextWindow
    factory.startReactNative(
      withModuleName: "main",
      in: nextWindow,
      launchOptions: nil)

    if !connectionOptions.urlContexts.isEmpty {
      self.scene(scene, openURLContexts: connectionOptions.urlContexts)
    }
  }

  func scene(_ scene: UIScene, openURLContexts URLContexts: Set<UIOpenURLContext>) {
    guard let context = URLContexts.first,
      let appDelegate = UIApplication.shared.delegate as? AppDelegate else {
      return
    }

    var options: [UIApplication.OpenURLOptionsKey: Any] = [
      .openInPlace: context.options.openInPlace,
    ]
    if let sourceApplication = context.options.sourceApplication {
      options[.sourceApplication] = sourceApplication
    }
    if let annotation = context.options.annotation {
      options[.annotation] = annotation
    }
    _ = appDelegate.application(
      UIApplication.shared,
      open: context.url,
      options: options)
  }

  func scene(_ scene: UIScene, continue userActivity: NSUserActivity) {
    guard let appDelegate = UIApplication.shared.delegate as? AppDelegate else {
      return
    }
    _ = appDelegate.application(
      UIApplication.shared,
      continue: userActivity,
      restorationHandler: { _ in })
  }
}
// @generated end ${TAG}
`;

const LEGACY_STARTUP_PATTERN =
  /#if os\(iOS\) \|\| os\(tvOS\)\n\s*window = UIWindow\(frame: UIScreen\.main\.bounds\)\n\s*factory\.startReactNative\(\n\s*withModuleName: "main",\n\s*in: window,\n\s*launchOptions: launchOptions\)\n#endif/;

function applySceneLifecycleToAppDelegate(contents) {
  if (contents.includes(TAG)) {
    return contents;
  }
  if (!LEGACY_STARTUP_PATTERN.test(contents)) {
    throw new Error(
      'Expo AppDelegate startup block was not found; review the UIScene patch against the new template.',
    );
  }

  let next = contents.replace(
    LEGACY_STARTUP_PATTERN,
    `#if os(iOS) || os(tvOS)
    if #unavailable(iOS 13.0) {
      window = UIWindow(frame: UIScreen.main.bounds)
      factory.startReactNative(
        withModuleName: "main",
        in: window,
        launchOptions: launchOptions)
    }
#endif`,
  );

  const linkingMarker = '\n  // Linking API';
  if (!next.includes(linkingMarker)) {
    throw new Error('Expo AppDelegate linking marker was not found for the UIScene configuration method.');
  }
  next = next.replace(linkingMarker, `\n${SCENE_CONFIGURATION_METHOD}\n  // Linking API`);

  const delegateMarker = '\nclass ReactNativeDelegate: ExpoReactNativeFactoryDelegate';
  if (!next.includes(delegateMarker)) {
    throw new Error('Expo ReactNativeDelegate marker was not found for SceneDelegate insertion.');
  }
  return next.replace(delegateMarker, `\n${SCENE_DELEGATE_CLASS}${delegateMarker}`);
}

function applySceneManifest(infoPlist) {
  infoPlist.UIApplicationSceneManifest = {
    UIApplicationSupportsMultipleScenes: false,
    UISceneConfigurations: {
      UIWindowSceneSessionRoleApplication: [
        {
          UISceneConfigurationName: 'Default Configuration',
          UISceneDelegateClassName: '$(PRODUCT_MODULE_NAME).SceneDelegate',
        },
      ],
    },
  };
  return infoPlist;
}

function withIosSceneLifecycle(config) {
  config = withInfoPlist(config, (mod) => {
    mod.modResults = applySceneManifest(mod.modResults);
    return mod;
  });
  return withAppDelegate(config, (mod) => {
    if (mod.modResults.language !== 'swift') {
      throw new Error('QueenZone UIScene lifecycle generation requires a Swift AppDelegate.');
    }
    mod.modResults.contents = applySceneLifecycleToAppDelegate(mod.modResults.contents);
    return mod;
  });
}

const plugin = createRunOncePlugin(withIosSceneLifecycle, TAG, '1.0.0');
plugin.applySceneLifecycleToAppDelegate = applySceneLifecycleToAppDelegate;
plugin.applySceneManifest = applySceneManifest;
plugin.TAG = TAG;
module.exports = plugin;
