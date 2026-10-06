import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const actionRoot = path.join(repo, '.github/actions');
const official = 'bitwarden/sm-action@1238aae8fc64b212641190a9227c8a734ab1a793';
const source = readFileSync(path.join(actionRoot, 'bitwarden-retry/action.yml'), 'utf8').replaceAll('\r\n', '\n');
if (source.split(official).length !== 4) throw new Error('Expected exactly three pinned fetches.');
let attempt = 0; let replacements = 0; let publishing = false;
const lines = source.replaceAll(official, './.github/actions/bitwarden-fake-fetch').split('\n');
const fixture = [];
for (const line of lines) {
  const id = /^    - id: fetch([123])$/.exec(line);
  if (id) attempt = Number(id[1]);
  if (line === '    - id: publish') publishing = true;
  let result = line;
  if (line.trim() === 'run: sleep 20' || line.trim() === 'run: sleep 60') {
    result = line.replace('sleep', 'echo Fixture simulated wait seconds'); replacements++;
  }
  if (publishing && line === '      uses: ./.github/actions/bitwarden-retry/guard') {
    result = '      uses: ./.github/actions/bitwarden-fake-publication';
  }
  fixture.push(result);
  if (line === "        set_env: 'false'") fixture.push(`        attempt: ${attempt}`);
}
if (replacements !== 2) throw new Error('Expected both production delays.');
function write(name, file, text) {
  const dir = path.join(actionRoot, name); mkdirSync(dir, { recursive: true });
  writeFileSync(path.join(dir, file), text);
}
write('bitwarden-contract-fixture', 'action.yml', fixture.join('\n'));
write('bitwarden-fake-fetch', 'action.yml', `name: Credential-free fake fetch\ndescription: Contract test only; no network or credentials.\ninputs:\n  access_token:\n    description: Ignored synthetic input\n  secrets:\n    description: Synthetic mapping\n  set_env:\n    description: Verify global export stays disabled\n  attempt:\n    description: Synthetic attempt number\nruns:\n  using: node24\n  main: index.cjs\n`);
write('bitwarden-fake-fetch', 'index.cjs', `
const fs = require('node:fs');
const attempt = Number(process.env.INPUT_ATTEMPT);
const mode = process.env.BITWARDEN_FIXTURE_CASE;
const values = { AZURE_WEBAPP_PUBLISH_PROFILE: 'fixture-profile', MOBILE_AUTH_SIGNING_KEY: 'fixture-key' };
const fail = mode === 'all_fail' || mode === 'second' && attempt === 1 || mode === 'third' && attempt < 3;
const partial = mode === 'partial' && attempt === 1;
if (process.env.INPUT_SET_ENV !== 'false') throw new Error('Global secret export must be disabled.');
for (const [name, value] of Object.entries(values)) {
  console.log('::add-mask::' + value);
  if (!partial || name === 'AZURE_WEBAPP_PUBLISH_PROFILE') fs.appendFileSync(process.env.GITHUB_OUTPUT, name + '=' + value + '\\n');
}
if (fail) { console.error('Expected synthetic fetch failure.'); process.exitCode = 1; }
`);
write('bitwarden-fake-publication', 'action.yml', readFileSync(path.join(actionRoot, 'bitwarden-retry/guard/action.yml'), 'utf8'));
write('bitwarden-fake-publication', 'index.cjs', `
const fs = require('node:fs');
const { execute } = require('../bitwarden-retry/guard/index.cjs');
try {
  execute({ phase: process.env.INPUT_PHASE, mapping: process.env.INPUT_MAPPING,
    data: process.env.FETCH_OUTPUTS, outputPath: process.env.GITHUB_OUTPUT,
    append: (file, text) => {
      fs.appendFileSync(file, text);
      if (process.env.BITWARDEN_FIXTURE_CASE === 'publication_failure') throw new Error('Synthetic failure after complete command-file write.');
    } });
} catch { console.error('Expected synthetic publication failure.'); process.exitCode = 1; }
`);
console.log('Prepared credential-free composite contract fixtures; official action will not execute.');
