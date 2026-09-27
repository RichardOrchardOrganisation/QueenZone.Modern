/** @type {import('jest').Config} */
module.exports = {
  preset: 'jest-expo',
  // @native-html/render pulls in ESM-only leaf packages (wooorm's
  // stringify-entities/character-entities-*) that jest-expo's default
  // transformIgnorePatterns does not cover; transform them too.
  transformIgnorePatterns: [
    '/node_modules/(?!(.pnpm|react-native|@react-native|@react-native-community|expo|@expo|@expo-google-fonts|react-navigation|@react-navigation|@sentry/react-native|native-base|standard-navigation|@native-html|@jsamr|stringify-entities|character-entities-html4|character-entities-legacy))',
    '/node_modules/react-native-reanimated/plugin/',
    '/node_modules/@react-native/babel-preset/',
  ],
  // Relative glob: `<rootDir>/src/**` misses every file on Windows because
  // Jest builds a mixed-slash path that micromatch does not match.
  testMatch: ['**/src/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  clearMocks: true,
  restoreMocks: true,
  // Coverage floors live in scripts/mobile-coverage-floors.json (enforced by
  // scripts/Test-MobileCoverageGate.mjs). Do not put thresholds here — this
  // runner is only one of two suites (#871 Option A).
  collectCoverageFrom: [
    '**/src/**/*.{ts,tsx}',
    '!**/src/**/*.test.{ts,tsx}',
    '!**/src/**/*.d.ts',
    '!**/src/test/**',
  ],
  coverageDirectory: 'coverage/jest',
  coverageReporters: ['json', 'json-summary', 'lcov', 'text-summary', 'cobertura'],
};
