import { cleanup } from '@testing-library/react-native';

jest.mock('@react-native-async-storage/async-storage', () =>
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);

jest.mock('expo-network', () => ({
  addNetworkStateListener: jest.fn(() => ({ remove: jest.fn() })),
  getNetworkStateAsync: jest.fn(async () => ({ isConnected: true, isInternetReachable: true })),
}));

jest.mock('@sentry/react-native', () => ({
  init: jest.fn(),
  captureException: jest.fn(),
  addBreadcrumb: jest.fn(),
  wrap: (app: unknown) => app,
  reactNavigationIntegration: jest.fn(() => ({
    registerNavigationContainer: jest.fn(),
  })),
}));

jest.mock('expo-font', () => ({
  useFonts: () => [true, null],
  isLoaded: () => true,
  loadAsync: jest.fn(),
}));

jest.mock('expo-splash-screen', () => ({
  preventAutoHideAsync: jest.fn(async () => {}),
  hideAsync: jest.fn(async () => {}),
}));

// eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
jest.mock('react-native-reanimated', () => require('./jest.reanimated.mock.js'));

// eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
jest.mock('react-native-gesture-handler', () => require('./jest.gesture-handler.mock.js'));

jest.mock('expo-share-intent', () => ({
  useShareIntent: () => ({
    isReady: true,
    hasShareIntent: false,
    shareIntent: { text: null, webUrl: null, files: null, type: null },
    resetShareIntent: jest.fn(),
    error: null,
  }),
  ShareIntentProvider: ({ children }: { children: unknown }) => children,
  useShareIntentContext: () => ({
    isReady: true,
    hasShareIntent: false,
    shareIntent: { text: null, webUrl: null, files: null, type: null },
    resetShareIntent: jest.fn(),
    error: null,
  }),
}));

jest.mock('expo-web-browser', () => ({
  maybeCompleteAuthSession: jest.fn(),
  openAuthSessionAsync: jest.fn(),
  openBrowserAsync: jest.fn(async () => ({ type: 'dismiss' })),
  dismissAuthSession: jest.fn(),
  dismissBrowser: jest.fn(),
  warmUpAsync: jest.fn(async () => {}),
  coolDownAsync: jest.fn(async () => {}),
}));

jest.mock('expo-task-manager', () => ({
  defineTask: jest.fn(),
  isTaskRegisteredAsync: jest.fn(async () => false),
  getRegisteredTasksAsync: jest.fn(async () => []),
}));

jest.mock('expo-background-task', () => ({
  BackgroundTaskStatus: { Restricted: 1, Available: 2 },
  BackgroundTaskResult: { Success: 1, Failed: 2 },
  registerTaskAsync: jest.fn(async () => {}),
  unregisterTaskAsync: jest.fn(async () => {}),
  getStatusAsync: jest.fn(async () => 2),
}));

jest.mock('expo-notifications', () => ({
  AndroidImportance: { DEFAULT: 3 },
  IosAuthorizationStatus: { NOT_DETERMINED: 0, DENIED: 1, AUTHORIZED: 2, PROVISIONAL: 3, EPHEMERAL: 4 },
  DEFAULT_ACTION_IDENTIFIER: 'expo.modules.notifications.actions.DEFAULT',
  getPermissionsAsync: jest.fn(async () => ({ granted: false, canAskAgain: true, status: 'undetermined' })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: false, canAskAgain: true, status: 'undetermined' })),
  getDevicePushTokenAsync: jest.fn(async () => ({ type: 'ios', data: 'mock-device-token' })),
  setNotificationChannelAsync: jest.fn(async () => null),
  setNotificationHandler: jest.fn(),
  addPushTokenListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationResponseReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  getLastNotificationResponseAsync: jest.fn(async () => null),
  clearLastNotificationResponseAsync: jest.fn(async () => {}),
}));

jest.mock('expo-image', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View } = require('react-native');
  const Image = View;
  Image.prefetch = jest.fn(async () => {});
  return { Image };
});

jest.mock('expo-media-library/legacy', () => ({
  getPermissionsAsync: jest.fn(async () => ({ granted: true, canAskAgain: true, status: 'granted' })),
  requestPermissionsAsync: jest.fn(async () => ({
    granted: true,
    canAskAgain: true,
    status: 'granted',
  })),
  saveToLibraryAsync: jest.fn(async () => {}),
}));

jest.mock('./modules/queenzone-wallpaper', () => ({
  setWallpaper: jest.fn(async () => {}),
}));

jest.mock('expo-sharing', () => ({
  isAvailableAsync: jest.fn(async () => true),
  shareAsync: jest.fn(async () => {}),
}));

jest.mock('expo-file-system/legacy', () => ({
  cacheDirectory: 'file:///cache/',
  EncodingType: { Base64: 'base64' },
  writeAsStringAsync: jest.fn(async () => {}),
}));

jest.mock('expo-file-system', () => {
  class FakeFile {
    uri: string;
    exists = false;
    size = 0;
    constructor(...parts: unknown[]) {
      this.uri = parts.map(String).join('/');
    }
    create() {
      this.exists = true;
    }
    delete() {
      this.exists = false;
      this.size = 0;
    }
    move() {}
    write() {}
    static createDownloadTask() {
      return { downloadAsync: async () => new FakeFile('file:///documents/x') };
    }
    static downloadFileAsync() {
      return Promise.resolve(new FakeFile('file:///documents/x'));
    }
  }
  class FakeDirectory {
    uri: string;
    exists = true;
    constructor(...parts: unknown[]) {
      this.uri = parts.map(String).join('/');
    }
    create() {
      this.exists = true;
    }
    list() {
      return [];
    }
  }
  return {
    File: FakeFile,
    Directory: FakeDirectory,
    Paths: {
      document: { uri: 'file:///documents' },
      cache: { uri: 'file:///cache' },
      availableDiskSpace: 64 * 1024 * 1024,
    },
  };
});

jest.mock('expo-audio', () => ({
  setAudioModeAsync: jest.fn(async () => {}),
  useAudioPlayer: () => ({
    play: jest.fn(),
    pause: jest.fn(),
    replace: jest.fn(),
  }),
  useAudioPlayerStatus: () => ({
    playing: false,
    isLoaded: true,
    currentTime: 0,
    duration: 0,
    didJustFinish: false,
    isBuffering: false,
    playbackState: 'ready',
    reasonForWaitingToPlay: '',
  }),
}));

jest.mock('lucide-react-native', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const React = require('react');
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View } = require('react-native');
  const Icon = () => React.createElement(View);
  return new Proxy(
    {},
    {
      get: () => Icon,
    },
  );
});

const originalError = console.error.bind(console);
const originalWarn = console.warn.bind(console);

const ignoredErrors = [/Require cycle:/, /was not wrapped in act\(/];

console.error = (...args: unknown[]) => {
  const text = args.map(String).join(' ');
  if (ignoredErrors.some((pattern) => pattern.test(text))) {
    if (/was not wrapped in act\(/.test(text)) {
      throw new Error(`Unexpected act warning:\n${text}`);
    }
    return;
  }
  originalError(...args);
};

console.warn = (...args: unknown[]) => {
  const text = args.map(String).join(' ');
  if (/was not wrapped in act\(/.test(text)) {
    throw new Error(`Unexpected act warning:\n${text}`);
  }
  originalWarn(...args);
};

afterEach(() => {
  cleanup();
});

const onUnhandled = (reason: unknown) => {
  throw reason instanceof Error ? reason : new Error(String(reason));
};

process.on('unhandledRejection', onUnhandled);
afterAll(() => {
  process.off('unhandledRejection', onUnhandled);
});
