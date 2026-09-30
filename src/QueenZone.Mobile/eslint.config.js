const { defineConfig, globalIgnores } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const globals = require('globals');

const screenErrorRules = {
  rules: {
    'no-swallowed-promise-error': {
      meta: {
        type: 'problem',
        messages: {
          swallowed: 'Handle failed screen work with an error state or an explicit fallback (#1840).',
        },
      },
      create(context) {
        return {
          CallExpression(node) {
            if (node.callee.type !== 'MemberExpression' || node.callee.property.name !== 'catch') return;
            const handler = node.arguments[0];
            if (handler?.type !== 'ArrowFunctionExpression') return;
            const body = handler.body;
            if (
              (body.type === 'Identifier' && body.name === 'undefined') ||
              (body.type === 'BlockStatement' && body.body.length === 0)
            ) {
              context.report({ node: handler, messageId: 'swallowed' });
            }
          },
        };
      },
    },
  },
};

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
      'react-hooks/preserve-manual-memoization': 'error',
      'react-hooks/immutability': 'error',
      'react-hooks/globals': 'error',
      // Latest-value refs and gesture refs span the query hooks and screens (#1821).
      'react-hooks/refs': 'off',
      // Existing effect-driven resets and loads need state-flow refactors (#1821).
      'react-hooks/set-state-in-effect': 'off',
      'react-hooks/purity': 'error',
    },
  },
  {
    files: ['src/widgets/OnThisDayWidget.ios.tsx'],
    rules: {
      // Expo serializes this JSC widget view, which selects a four-hour face from the clock (#1821).
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
  {
    files: ['src/screens/forum/**/*.{ts,tsx}', 'src/screens/photos/**/*.{ts,tsx}'],
    ignores: ['**/*.test.ts', '**/*.test.tsx'],
    plugins: { 'screen-errors': screenErrorRules },
    rules: {
      'screen-errors/no-swallowed-promise-error': 'error',
    },
  },
]);
