// The config plugin is CommonJS because Expo loads it in Node during prebuild.
// eslint-disable-next-line @typescript-eslint/no-require-imports -- CJS config plugin loaded by Expo in Node.
const sceneLifecycle = require('../../plugins/withIosSceneLifecycle.cjs') as {
  applySceneLifecycleToAppDelegate: (contents: string) => string;
  applySceneManifest: (infoPlist: Record<string, unknown>) => Record<string, unknown>;
};

const expoAppDelegate = `internal import Expo
import React

@main
class AppDelegate: ExpoAppDelegate {
  var window: UIWindow?
  var reactNativeFactory: RCTReactNativeFactory?

  public override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
  ) -> Bool {
    let factory = ExpoReactNativeFactory(delegate: ReactNativeDelegate())
    reactNativeFactory = factory
#if os(iOS) || os(tvOS)
    window = UIWindow(frame: UIScreen.main.bounds)
    factory.startReactNative(
      withModuleName: "main",
      in: window,
      launchOptions: launchOptions)
#endif
    return true
  }

  // Linking API
}

class ReactNativeDelegate: ExpoReactNativeFactoryDelegate {}
`;

describe('iOS 27 scene lifecycle config plugin', () => {
  it('moves React Native startup into a generated SceneDelegate and stays idempotent', () => {
    const first = sceneLifecycle.applySceneLifecycleToAppDelegate(expoAppDelegate);

    expect(first).toContain('configurationForConnecting connectingSceneSession');
    expect(first).toContain('class SceneDelegate: UIResponder, UIWindowSceneDelegate');
    expect(first).toContain('let nextWindow = UIWindow(windowScene: windowScene)');
    expect(first).toContain('appDelegate.window = nextWindow');
    expect(first).toContain('scene(_ scene: UIScene, openURLContexts');
    expect(first).toContain('scene(_ scene: UIScene, continue userActivity');
    expect(first).toContain('if #unavailable(iOS 13.0)');
    expect(sceneLifecycle.applySceneLifecycleToAppDelegate(first)).toBe(first);
  });

  it('adds one non-multiple-window scene configuration to Info.plist', () => {
    const plist = sceneLifecycle.applySceneManifest({ CFBundleDisplayName: 'QueenZone' });

    expect(plist).toMatchObject({
      CFBundleDisplayName: 'QueenZone',
      UIApplicationSceneManifest: {
        UIApplicationSupportsMultipleScenes: false,
        UISceneConfigurations: {
          UIWindowSceneSessionRoleApplication: [
            {
              UISceneConfigurationName: 'Default Configuration',
              UISceneDelegateClassName: '$(PRODUCT_MODULE_NAME).SceneDelegate',
            },
          ],
        },
      },
    });
  });

  it('fails loudly when Expo changes the generated startup shape', () => {
    expect(() => sceneLifecycle.applySceneLifecycleToAppDelegate('class AppDelegate {}')).toThrow(
      /startup block was not found/,
    );
  });
});
