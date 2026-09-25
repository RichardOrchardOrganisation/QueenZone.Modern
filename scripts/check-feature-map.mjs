#!/usr/bin/env node
/**
 * Feature-map coverage and freshness check (#1800).
 *
 * Node 24, no npm dependencies. Fails when a mobile screen or public/member
 * Razor page is missing from docs/feature-map/, or when an entry points at a
 * file, flow, spec, testId, or #id selector that no longer exists.
 *
 *   node scripts/check-feature-map.mjs
 *   node scripts/check-feature-map.mjs --write
 *   node scripts/check-feature-map.mjs --resolve mobile.photos.viewer
 *   node scripts/check-feature-map.mjs --flows mobile.photos.viewer
 */
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const MOBILE_AREAS = ['home', 'news', 'photos', 'archive', 'forum', 'account', 'messages', 'auth'];
const WEB_AREAS = [...MOBILE_AREAS, 'static'];
const SIGN_IN = new Set(['none', 'member', 'admin']);
const STACK_ALIAS = {
  Home: 'HomeStack',
  News: 'NewsStack',
  Photos: 'PhotosStack',
  Archive: 'ArchiveStack',
  Forum: 'ForumStack',
  RootStack: 'RootStack',
};

export function repoRootFrom(moduleUrl = import.meta.url) {
  return path.resolve(path.dirname(fileURLToPath(moduleUrl)), '..');
}

export function toPosix(filePath) {
  return String(filePath || '').replaceAll('\\', '/');
}

export function normalizeNewlines(text) {
  return String(text || '').replace(/\r\n/g, '\n');
}

function walkFiles(dir, predicate, out = []) {
  if (!existsSync(dir)) {
    return out;
  }
  for (const name of readdirSync(dir)) {
    const full = path.join(dir, name);
    const stat = statSync(full);
    if (stat.isDirectory()) {
      walkFiles(full, predicate, out);
    } else if (predicate(full, name)) {
      out.push(full);
    }
  }
  return out;
}

function readText(filePath) {
  return readFileSync(filePath, 'utf8');
}

function asList(value) {
  if (value == null) {
    return [];
  }
  return Array.isArray(value) ? value : [value];
}

export function parseTestIdKeys(source) {
  const keys = new Set();
  const block = String(source || '').match(/export const testIds\s*=\s*\{([\s\S]*?)\}\s*as const/);
  if (!block) {
    return keys;
  }
  for (const match of block[1].matchAll(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:/gm)) {
    keys.add(match[1]);
  }
  return keys;
}

export function parseScreenRegistrations(stacksSource, rootSource) {
  const registrations = [];
  const usedComponents = new Set();

  const takeScreens = (source, defaultStack) => {
    const tag = /<(\w+)\.Screen\b([\s\S]*?)\/>/g;
    let match;
    while ((match = tag.exec(source))) {
      const attrs = match[2];
      const name = attrs.match(/\bname=["']([^"']+)["']/)?.[1];
      const component = attrs.match(/\bcomponent=\{(\w+)\}/)?.[1];
      if (!name || !component) {
        continue;
      }
      const stack = STACK_ALIAS[match[1]] || (defaultStack && match[1] === defaultStack.replace(/Stack$/, '') ? defaultStack : match[1]);
      const screen = `${stack}/${name}`;
      registrations.push({ screen, name, component, stack });
      usedComponents.add(component);
    }
  };

  takeScreens(stacksSource, null);
  takeScreens(rootSource, 'RootStack');

  const commonFn = stacksSource.match(/export function commonScreens\([\s\S]*?\n\}/);
  const commonBody = commonFn ? commonFn[0] : '';
  const commonAlways = [];
  const commonByStory = { news: [], archive: [] };
  for (const match of commonBody.matchAll(/<Screen\b([\s\S]*?)\/>/g)) {
    const attrs = match[1];
    const name = attrs.match(/\bname=["']([^"']+)["']/)?.[1];
    const component = attrs.match(/\bcomponent=\{(\w+)\}/)?.[1];
    if (!name || !component) {
      continue;
    }
    if (/story === ['"]news['"]/.test(match[0]) || /options\?\.story === ['"]news['"]/.test(commonBody.slice(0, match.index))) {
      // Fall through to ternary detection below.
    }
    commonAlways.push({ name, component });
  }

  // commonScreens always registers Search; Story is gated by options.story.
  const search = commonBody.match(/<Screen name="Search" component=\{(\w+)\}/);
  const newsStory = commonBody.match(/story === ['"]news['"][\s\S]*?<Screen name="Story" component=\{(\w+)\}/);
  const archiveStory = commonBody.match(/story === ['"]archive['"][\s\S]*?<Screen name="Story" component=\{(\w+)\}/);

  commonAlways.length = 0;
  commonByStory.news.length = 0;
  commonByStory.archive.length = 0;
  if (search) {
    commonAlways.push({ name: 'Search', component: search[1] });
    usedComponents.add(search[1]);
  }
  if (newsStory) {
    commonByStory.news.push({ name: 'Story', component: newsStory[1] });
    usedComponents.add(newsStory[1]);
  }
  if (archiveStory) {
    commonByStory.archive.push({ name: 'Story', component: archiveStory[1] });
    usedComponents.add(archiveStory[1]);
  }

  for (const match of stacksSource.matchAll(/commonScreens\((\w+)(?:,\s*\{\s*story:\s*['"](\w+)['"]\s*\})?\)/g)) {
    const stack = STACK_ALIAS[match[1]] || `${match[1]}Stack`;
    for (const item of commonAlways) {
      registrations.push({ screen: `${stack}/${item.name}`, name: item.name, component: item.component, stack });
    }
    if (match[2] && commonByStory[match[2]]) {
      for (const item of commonByStory[match[2]]) {
        registrations.push({ screen: `${stack}/${item.name}`, name: item.name, component: item.component, stack });
      }
    }
  }

  return { registrations, usedComponents };
}

export function listScreenFiles(screensDir) {
  return walkFiles(screensDir, (full, name) => name.endsWith('Screen.tsx') && !name.endsWith('.test.tsx')).sort();
}

export function exportedNames(source) {
  const names = new Set();
  for (const match of String(source || '').matchAll(/export function (\w+)/g)) {
    names.add(match[1]);
  }
  for (const match of String(source || '').matchAll(/export const (\w+)/g)) {
    names.add(match[1]);
  }
  return names;
}

export function listPageFiles(pagesDir) {
  return walkFiles(pagesDir, (full, name) => name.endsWith('.cshtml') && !name.startsWith('_')).sort();
}

export function pageDirective(source) {
  const match = String(source || '').match(/^@page(?:\s+"([^"]*)")?/m);
  if (!match) {
    return null;
  }
  return { url: match[1] || '' };
}

export function minimatchPrefix(relativePosix, pattern) {
  const spec = String(pattern || '').replaceAll('\\', '/');
  if (spec.endsWith('/**')) {
    const prefix = spec.slice(0, -3);
    return relativePosix === prefix || relativePosix.startsWith(`${prefix}/`);
  }
  return relativePosix === spec;
}

function loadJsonFile(filePath) {
  try {
    return JSON.parse(readText(filePath));
  } catch (error) {
    throw new Error(`${toPosix(filePath)} is not valid JSON: ${error.message}`);
  }
}

export function loadFeatureMap(root) {
  const mapRoot = path.join(root, 'docs', 'feature-map');
  const excludedPath = path.join(mapRoot, 'excluded.json');
  if (!existsSync(excludedPath)) {
    throw new Error('docs/feature-map/excluded.json is missing the single Admin exclusion block.');
  }
  const excludedDoc = loadJsonFile(excludedPath);
  const excluded = asList(excludedDoc.excluded);
  if (excluded.length !== 1) {
    throw new Error('docs/feature-map/excluded.json must contain exactly one excluded block.');
  }
  if (!excluded[0]?.pattern || !excluded[0]?.reason) {
    throw new Error('The excluded block needs pattern and reason.');
  }

  const entries = [];
  const files = [];
  const loadArea = (surface, area) => {
    const rel = `docs/feature-map/${surface}/${area}.json`;
    const full = path.join(root, rel);
    files.push(rel);
    if (!existsSync(full)) {
      throw new Error(`Missing feature-map file ${rel}.`);
    }
    const doc = loadJsonFile(full);
    const areaEntries = asList(doc.entries);
    for (const entry of areaEntries) {
      entries.push({ ...entry, _surface: surface, _area: area, _file: rel });
    }
    return doc;
  };

  const mobile = {};
  const web = {};
  for (const area of MOBILE_AREAS) {
    mobile[area] = loadArea('mobile', area);
  }
  for (const area of WEB_AREAS) {
    web[area] = loadArea('web', area);
  }

  return { root: mapRoot, excluded, entries, files, mobile, web };
}

export function entryScreens(entry) {
  return asList(entry.screen).filter(Boolean);
}

export function generateIndexMarkdown(map) {
  const lines = [
    '# Feature map',
    '',
    'Machine-readable map of every mobile screen and public or member web page.',
    'Agents and the verify skills read these JSON files; do not hand-edit this index.',
    'Regenerate with `node scripts/check-feature-map.mjs --write`.',
    '',
  ];

  const renderSurface = (title, areas, surface) => {
    lines.push(`## ${title}`, '');
    for (const area of areas) {
      const doc = surface === 'mobile' ? map.mobile[area] : map.web[area];
      const areaEntries = asList(doc?.entries).slice().sort((a, b) => String(a.id).localeCompare(String(b.id)));
      lines.push(`### ${area}`, '');
      if (areaEntries.length === 0) {
        lines.push('_No entries._', '');
        continue;
      }
      for (const entry of areaEntries) {
        const target = entry.screen
          ? asList(entry.screen).join(', ')
          : [entry.page, entry.url].filter(Boolean).join(' — ');
        lines.push(`- \`${entry.id}\` — ${entry.name || entry.id}${target ? ` (${target})` : ''}`);
      }
      lines.push('');
    }
  };

  renderSurface('Mobile', MOBILE_AREAS, 'mobile');
  renderSurface('Web', WEB_AREAS, 'web');
  lines.push('## Excluded', '');
  for (const item of map.excluded) {
    lines.push(`- \`${item.pattern}\` — ${item.reason}`);
  }
  lines.push('');
  return `${lines.join('\n')}`;
}

export function resolveFeature(map, idOrAlias) {
  const needle = String(idOrAlias || '').trim();
  if (!needle) {
    return null;
  }
  const exact = map.entries.find((entry) => entry.id === needle);
  if (exact) {
    return exact;
  }
  return (
    map.entries.find((entry) => asList(entry.aliases).includes(needle)) ||
    map.entries.find((entry) => {
      const drive = entry.drive;
      if (typeof drive === 'string') {
        return drive === needle;
      }
      if (drive && typeof drive === 'object') {
        return Object.prototype.hasOwnProperty.call(drive, needle);
      }
      return false;
    }) ||
    null
  );
}

export function resolveDriveFlow(entry, flowName) {
  if (!entry) {
    return null;
  }
  const drive = entry.drive;
  if (drive && typeof drive === 'object' && drive[flowName]) {
    return drive[flowName];
  }
  const flows = asList(entry.flows);
  if (typeof drive === 'string' && drive === flowName) {
    return flows[0] || null;
  }
  if (asList(entry.aliases).includes(flowName)) {
    return flows[0] || null;
  }
  return flows[0] || null;
}

export function listUiSourcePaths(map) {
  const sources = new Set();
  for (const entry of map.entries) {
    if (entry._surface !== 'mobile') {
      continue;
    }
    for (const source of asList(entry.sources)) {
      sources.add(toPosix(source));
    }
  }
  return sources;
}

function isLeafScreenComponent(name) {
  return /Screen$/.test(name) || name === 'SearchRouteScreen';
}

export function checkFeatureMap({ root, write = false } = {}) {
  const errors = [];
  const map = loadFeatureMap(root);
  const ids = new Set();

  for (const entry of map.entries) {
    if (!entry.id || typeof entry.id !== 'string') {
      errors.push(`${entry._file}: an entry is missing id.`);
      continue;
    }
    if (ids.has(entry.id)) {
      errors.push(`Duplicate feature id ${entry.id}.`);
    }
    ids.add(entry.id);
    if (!entry.name) {
      errors.push(`${entry.id}: missing name.`);
    }
    if (!Array.isArray(entry.aliases)) {
      errors.push(`${entry.id}: aliases must be an array.`);
    }
    if (!entry.entry) {
      errors.push(`${entry.id}: missing entry (user path).`);
    }
    if (!Array.isArray(entry.sources) || entry.sources.length === 0) {
      errors.push(`${entry.id}: sources[] is required.`);
    }
    if (!SIGN_IN.has(entry.signIn)) {
      errors.push(`${entry.id}: signIn must be none|member|admin.`);
    }
    if (entry._surface === 'mobile' && entryScreens(entry).length === 0) {
      errors.push(`${entry.id}: mobile entries need screen.`);
    }
    if (entry._surface === 'web') {
      if (!entry.page) {
        errors.push(`${entry.id}: web entries need page.`);
      }
      if (typeof entry.url !== 'string') {
        errors.push(`${entry.id}: web entries need url.`);
      }
      const pageName = toPosix(entry.page || '');
      const isAction = /\/Action\.cshtml$/.test(pageName);
      const isLogout = /\/Logout\.cshtml$/.test(pageName);
      if ((isAction || isLogout) && entry.kind !== 'handler') {
        errors.push(`${entry.id}: Logout and */Action pages must set kind: handler.`);
      }
    }

    for (const source of asList(entry.sources)) {
      if (!existsSync(path.join(root, source))) {
        errors.push(`${entry.id}: source missing ${source}.`);
      }
    }
    for (const flow of asList(entry.flows)) {
      if (!existsSync(path.join(root, flow))) {
        errors.push(`${entry.id}: flow missing ${flow}.`);
      }
    }
    for (const spec of asList(entry.specs)) {
      if (!existsSync(path.join(root, spec))) {
        errors.push(`${entry.id}: spec missing ${spec}.`);
      }
    }
    if (entry.recipe && !existsSync(path.join(root, entry.recipe))) {
      errors.push(`${entry.id}: recipe missing ${entry.recipe}.`);
    }
    if (entry.drive && typeof entry.drive === 'object') {
      for (const [name, flow] of Object.entries(entry.drive)) {
        if (!existsSync(path.join(root, flow))) {
          errors.push(`${entry.id}: drive.${name} missing ${flow}.`);
        }
      }
    }
  }

  const stacksPath = path.join(root, 'src/QueenZone.Mobile/src/navigation/stacks.tsx');
  const rootNavPath = path.join(root, 'src/QueenZone.Mobile/src/navigation/RootNavigator.tsx');
  const { registrations, usedComponents } = parseScreenRegistrations(readText(stacksPath), readText(rootNavPath));
  const mappedScreens = new Set();
  for (const entry of map.entries) {
    for (const screen of entryScreens(entry)) {
      mappedScreens.add(screen);
    }
  }
  for (const registration of registrations) {
    if (!isLeafScreenComponent(registration.component)) {
      continue;
    }
    if (!mappedScreens.has(registration.screen)) {
      errors.push(`Unmapped screen registration ${registration.screen} (${registration.component}).`);
    }
  }

  const screensDir = path.join(root, 'src/QueenZone.Mobile/src/screens');
  const mappedSourceFiles = new Set();
  for (const entry of map.entries) {
    for (const source of asList(entry.sources)) {
      mappedSourceFiles.add(toPosix(source));
    }
  }
  for (const file of listScreenFiles(screensDir)) {
    const rel = toPosix(path.relative(root, file));
    const names = exportedNames(readText(file));
    const registered = [...names].some((name) => usedComponents.has(name));
    if (!registered && !mappedSourceFiles.has(rel)) {
      errors.push(`Screen file is not registered or mapped: ${rel}.`);
    }
  }

  const testIdsPath = path.join(root, 'src/QueenZone.Mobile/src/test/testIds.ts');
  const testIdKeys = parseTestIdKeys(readText(testIdsPath));
  for (const entry of map.entries) {
    for (const key of asList(entry.testIds)) {
      if (!testIdKeys.has(key)) {
        errors.push(`${entry.id}: testIds key '${key}' is missing from testIds.ts.`);
      }
    }
  }

  const pagesDir = path.join(root, 'src/QueenZone.Web/Pages');
  const mappedPages = new Set();
  for (const entry of map.entries) {
    if (entry.page) {
      mappedPages.add(toPosix(entry.page));
    }
  }
  for (const file of listPageFiles(pagesDir)) {
    const relFromWeb = toPosix(path.relative(path.join(root, 'src/QueenZone.Web'), file));
    if (!pageDirective(readText(file))) {
      continue;
    }
    const excluded = map.excluded.some((item) => minimatchPrefix(relFromWeb, item.pattern));
    if (excluded) {
      continue;
    }
    if (!mappedPages.has(relFromWeb)) {
      errors.push(`Unmapped Razor page ${relFromWeb}.`);
    }
  }

  const sharedDir = path.join(root, 'src/QueenZone.Web/Pages/Shared');
  const sharedText = walkFiles(sharedDir, (full, name) => name.endsWith('.cshtml'))
    .map((file) => readText(file))
    .join('\n');
  for (const entry of map.entries) {
    for (const selector of asList(entry.selectors)) {
      if (!selector.startsWith('#')) {
        errors.push(`${entry.id}: selector '${selector}' must be a #id.`);
        continue;
      }
      const id = selector.slice(1);
      const idPattern = new RegExp(`\\bid=["']${id}["']`);
      const inSources = asList(entry.sources).some((source) => {
        const full = path.join(root, source);
        return existsSync(full) && idPattern.test(readText(full));
      });
      if (!inSources && !idPattern.test(sharedText)) {
        errors.push(`${entry.id}: #id selector '${selector}' was not found in sources or Pages/Shared.`);
      }
    }
  }

  const indexPath = path.join(root, 'docs/feature-map/README.md');
  const expected = generateIndexMarkdown(map);
  const actual = existsSync(indexPath) ? normalizeNewlines(readText(indexPath)) : '';
  if (write) {
    mkdirSync(path.dirname(indexPath), { recursive: true });
    writeFileSync(indexPath, expected.endsWith('\n') ? expected : `${expected}\n`);
  } else if (normalizeNewlines(actual) !== normalizeNewlines(expected.endsWith('\n') ? expected : `${expected}\n`)) {
    errors.push('docs/feature-map/README.md is stale. Run node scripts/check-feature-map.mjs --write.');
  }

  return { ok: errors.length === 0, errors, map, index: expected };
}

function printFlows(entry) {
  for (const flow of asList(entry?.flows)) {
    console.log(flow);
  }
}

function parseArgs(argv) {
  const args = { write: false, resolve: null, flows: null };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--write') {
      args.write = true;
    } else if (arg === '--resolve') {
      args.resolve = argv[i + 1] ?? '';
      i += 1;
    } else if (arg === '--flows') {
      args.flows = argv[i + 1] ?? '';
      i += 1;
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return args;
}

export function main(argv = process.argv.slice(2), { root = repoRootFrom(), stdout = console.log, stderr = console.error } = {}) {
  const args = parseArgs(argv);
  const map = loadFeatureMap(root);
  if (args.resolve != null) {
    const entry = resolveFeature(map, args.resolve);
    if (!entry) {
      stderr(`Unknown feature '${args.resolve}'.`);
      return 1;
    }
    stdout(JSON.stringify(entry, null, 2));
    return 0;
  }
  if (args.flows != null) {
    const entry = resolveFeature(map, args.flows);
    if (!entry) {
      stderr(`Unknown feature '${args.flows}'.`);
      return 1;
    }
    printFlows(entry);
    return 0;
  }
  const result = checkFeatureMap({ root, write: args.write });
  if (!result.ok) {
    stderr(result.errors.join('\n'));
    return 1;
  }
  stdout(args.write ? 'Feature map index written.' : 'Feature map ok.');
  return 0;
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly) {
  process.exitCode = main();
}
