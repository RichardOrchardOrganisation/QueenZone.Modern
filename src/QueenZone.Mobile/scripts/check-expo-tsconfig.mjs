import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);

const local = JSON.parse(readFileSync(new URL('../tsconfig.base.json', import.meta.url), 'utf8'));
const expo = JSON.parse(readFileSync(require.resolve('expo/tsconfig.base'), 'utf8'));

for (const field of ['compilerOptions', 'exclude']) {
  if (JSON.stringify(local[field]) !== JSON.stringify(expo[field])) {
    throw new Error(`tsconfig.base.json ${field} differs from Expo's base. Sync it after updating Expo.`);
  }
}

console.log('Local TypeScript base matches Expo.');
