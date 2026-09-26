const { defineConfig, globalIgnores } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const globals = require('globals');

module.exports = defineConfig([
  globalIgnores([
    'ios/**',
    'android/**',
    'modules/**/android/**',
    'node_modules/**',
    'coverage/**',
    '.expo/**',
  ]),
  expoConfig,
  {
    files: [
      'scripts/**',
      'plugins/**',
      'contracts/**',
      '*.cjs',
      'app.config.ts',
      'babel.config.js',
      'eslint.config.js',
      'jest.config.js',
      'jest.*.js',
      'jest.setup.ts',
    ],
    languageOptions: {
      globals: globals.node,
    },
  },
  {
    rules: {
      // Expo/flat recommended leaves exhaustive-deps as warn; this gate is error-first (#1140).
      'react-hooks/exhaustive-deps': 'error',
      'react-hooks/rules-of-hooks': 'error',
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/content/sample', '**/content/sample.*'],
              message:
                'src/content/sample.ts was removed (#1147). Use src/content/archiveHub.ts or src/content/newsDecades.ts for static config; leftover fixture data is src/test/fixtures/sample.ts.',
            },
          ],
        },
      ],
      // eslint-plugin-react-hooks@7 recommended also ships React Compiler rules. These six
      // still have violations; each is switched back on by #1821. Do not add to this list.
      'react-hooks/preserve-manual-memoization': 'off',
      'react-hooks/immutability': 'off',
      'react-hooks/globals': 'off',
      'react-hooks/refs': 'off',
      'react-hooks/set-state-in-effect': 'off',
      'react-hooks/purity': 'off',
    },
  },
  {
    files: ['src/screens/**/*.{ts,tsx}', 'src/ui/**/*.{ts,tsx}'],
    ignores: ['**/*.test.ts', '**/*.test.tsx'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/content/sample', '**/content/sample.*'],
              message:
                'src/content/sample.ts was removed (#1147). Use src/content/archiveHub.ts or src/content/newsDecades.ts for static config; leftover fixture data is src/test/fixtures/sample.ts.',
            },
            {
              group: ['**/test/fixtures/**'],
              message: 'Do not import test fixtures from production screens or UI (#1147).',
            },
          ],
        },
      ],
    },
  },
]);
