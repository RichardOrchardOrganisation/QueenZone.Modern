import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  checkFeatureMap,
  generateIndexMarkdown,
  loadFeatureMap,
  main,
  listPageFiles,
  listScreenFiles,
  parseScreenRegistrations,
  parseTestIdKeys,
  resolveFeature,
  resolveDriveFlow,
} from './check-feature-map.mjs';

const stacks = `
export function commonScreens(Stack, options) {
  const Screen = Stack.Screen;
  return (
    <>
      <Screen name="Search" component={SearchRouteScreen} />
      {options?.story === 'news' ? <Screen name="Story" component={NewsStoryScreen} /> : null}
      {options?.story === 'archive' ? <Screen name="Story" component={StoryScreen} /> : null}
    </>
  );
}
export function HomeStack() {
  return (
    <Home.Navigator>
      <Home.Screen name="Home" component={HomeScreen} />
      {commonScreens(Home, { story: 'news' })}
    </Home.Navigator>
  );
}
`;

const rootNav = `
export function RootNavigator() {
  return (
    <RootStack.Navigator>
      <RootStack.Screen name="Tabs" component={MainTabs} />
      <RootStack.Screen name="SignIn" component={SignInScreen} />
    </RootStack.Navigator>
  );
}
`;

test('parseTestIdKeys reads object keys', () => {
  const keys = parseTestIdKeys(`export const testIds = {\n  photoViewerScreen: 'photo-viewer-screen',\n} as const;`);
  assert.ok(keys.has('photoViewerScreen'));
});

test('parseTestIdKeys ignores a long non-key line quickly', () => {
  const noise = `export const testIds = {\n  photoViewerScreen: 'photo-viewer-screen',\n  ${' '.repeat(20000)}notAKey\n} as const;`;
  const started = performance.now();
  const keys = parseTestIdKeys(noise);
  assert.ok(performance.now() - started < 100);
  assert.deepEqual([...keys], ['photoViewerScreen']);
});

test('screen and page lists use an explicit text order', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'feature-map-sort-'));
  write(root, 'screens/ZedScreen.tsx', '');
  write(root, 'screens/AlphaScreen.tsx', '');
  write(root, 'screens/AlphaScreen.test.tsx', '');
  write(root, 'pages/Zed.cshtml', '');
  write(root, 'pages/_Partial.cshtml', '');
  write(root, 'pages/Alpha.cshtml', '');
  assert.deepEqual(
    listScreenFiles(path.join(root, 'screens')).map((file) => path.basename(file)),
    ['AlphaScreen.tsx', 'ZedScreen.tsx'],
  );
  assert.deepEqual(
    listPageFiles(path.join(root, 'pages')).map((file) => path.basename(file)),
    ['Alpha.cshtml', 'Zed.cshtml'],
  );
});

test('parseScreenRegistrations includes commonScreens', () => {
  const { registrations, usedComponents } = parseScreenRegistrations(stacks, rootNav);
  const names = registrations.map((item) => item.screen);
  assert.ok(names.includes('HomeStack/Home'));
  assert.ok(names.includes('HomeStack/Search'));
  assert.ok(names.includes('HomeStack/Story'));
  assert.ok(names.includes('RootStack/SignIn'));
  assert.ok(usedComponents.has('SearchRouteScreen'));
  assert.ok(usedComponents.has('NewsStoryScreen'));
});

function write(root, rel, contents) {
  const full = path.join(root, rel);
  mkdirSync(path.dirname(full), { recursive: true });
  writeFileSync(full, contents);
}

function fixtureRoot() {
  const root = mkdtempSync(path.join(tmpdir(), 'feature-map-'));
  write(root, 'src/QueenZone.Mobile/src/navigation/stacks.tsx', stacks);
  write(root, 'src/QueenZone.Mobile/src/navigation/RootNavigator.tsx', rootNav);
  write(
    root,
    'src/QueenZone.Mobile/src/screens/home/HomeScreen.tsx',
    'export function HomeScreen() { return null; }\n',
  );
  write(
    root,
    'src/QueenZone.Mobile/src/screens/news/NewsStoryScreen.tsx',
    'export function NewsStoryScreen() { return null; }\n',
  );
  write(
    root,
    'src/QueenZone.Mobile/src/screens/archive/SearchScreen.tsx',
    'export function SearchRouteScreen() { return null; }\n',
  );
  write(
    root,
    'src/QueenZone.Mobile/src/screens/account/SignInScreen.tsx',
    'export function SignInScreen() { return null; }\n',
  );
  write(root, 'src/QueenZone.Mobile/src/test/testIds.ts', 'export const testIds = {\n  homeScreen: "home-screen",\n} as const;\n');
  write(root, 'src/QueenZone.Web/Pages/Index.cshtml', '@page "/"\n<h1 id="qz-hero-archive">Home</h1>\n');
  write(root, 'src/QueenZone.Web/Pages/Account/Logout.cshtml', '@page "/account/logout"\n');
  write(root, 'src/QueenZone.Web/Pages/Admin/Index.cshtml', '@page "/admin"\n');
  write(root, 'src/QueenZone.Web/Pages/Shared/_SiteHeader.cshtml', '<div id="qz-mobile-menu"></div>\n');
  write(root, 'src/QueenZone.Mobile/maestro/flows/01-launch.yaml', 'appId: org.queenzone.mobile\n');
  const mobileHome = {
    area: 'home',
    surface: 'mobile',
    entries: [
      {
        id: 'mobile.home.home',
        name: 'Home',
        aliases: ['homepage'],
        screen: 'HomeStack/Home',
        entry: 'Launch the app',
        sources: ['src/QueenZone.Mobile/src/screens/home/HomeScreen.tsx'],
        testIds: ['homeScreen'],
        flows: ['src/QueenZone.Mobile/maestro/flows/01-launch.yaml'],
        drive: { launch: 'src/QueenZone.Mobile/maestro/flows/01-launch.yaml' },
        signIn: 'none',
      },
      {
        id: 'mobile.news.story',
        name: 'News story',
        aliases: [],
        screen: 'HomeStack/Story',
        entry: 'Home → story',
        sources: ['src/QueenZone.Mobile/src/screens/news/NewsStoryScreen.tsx'],
        testIds: [],
        flows: [],
        signIn: 'none',
      },
      {
        id: 'mobile.archive.search',
        name: 'Search',
        aliases: ['search'],
        screen: 'HomeStack/Search',
        entry: 'Home search',
        sources: ['src/QueenZone.Mobile/src/screens/archive/SearchScreen.tsx'],
        testIds: [],
        flows: [],
        signIn: 'none',
      },
      {
        id: 'mobile.auth.signIn',
        name: 'Sign in',
        aliases: [],
        screen: 'RootStack/SignIn',
        entry: 'Profile → Sign in',
        sources: ['src/QueenZone.Mobile/src/screens/account/SignInScreen.tsx'],
        testIds: [],
        flows: [],
        signIn: 'none',
      },
    ],
  };
  for (const area of ['home', 'news', 'photos', 'archive', 'forum', 'account', 'messages', 'auth']) {
    write(
      root,
      `docs/feature-map/mobile/${area}.json`,
      JSON.stringify({ area, surface: 'mobile', entries: mobileHome.entries.filter((item) => item.id.startsWith(`mobile.${area}.`)) }, null, 2),
    );
  }
  write(
    root,
    'docs/feature-map/web/home.json',
    JSON.stringify(
      {
        area: 'home',
        surface: 'web',
        entries: [
          {
            id: 'web.home.index',
            name: 'Homepage',
            aliases: [],
            page: 'Pages/Index.cshtml',
            url: '/',
            entry: 'Open /',
            sources: ['src/QueenZone.Web/Pages/Index.cshtml'],
            selectors: ['#qz-hero-archive'],
            specs: [],
            signIn: 'none',
          },
        ],
      },
      null,
      2,
    ),
  );
  write(
    root,
    'docs/feature-map/web/auth.json',
    JSON.stringify(
      {
        area: 'auth',
        surface: 'web',
        entries: [
          {
            id: 'web.auth.logout',
            name: 'Sign out',
            aliases: [],
            page: 'Pages/Account/Logout.cshtml',
            url: '/account/logout',
            entry: 'Sign out',
            sources: ['src/QueenZone.Web/Pages/Account/Logout.cshtml'],
            kind: 'handler',
            selectors: [],
            selectorsReason: 'Handler: verify the redirect and session change.',
            signIn: 'member',
          },
        ],
      },
      null,
      2,
    ),
  );
  for (const area of ['news', 'photos', 'archive', 'forum', 'account', 'messages', 'static']) {
    write(root, `docs/feature-map/web/${area}.json`, JSON.stringify({ area, surface: 'web', entries: [] }, null, 2));
  }
  write(
    root,
    'docs/feature-map/excluded.json',
    JSON.stringify(
      {
        excluded: [
          {
            pattern: 'Pages/Admin/**',
            reason: 'Public and member pages only.',
          },
        ],
      },
      null,
      2,
    ),
  );
  return root;
}

test('checkFeatureMap accepts a complete fixture and --write', () => {
  const root = fixtureRoot();
  const first = checkFeatureMap({ root, write: true });
  assert.equal(first.ok, true, first.errors.join('\n'));
  const second = checkFeatureMap({ root, write: false });
  assert.equal(second.ok, true, second.errors.join('\n'));
  const map = loadFeatureMap(root);
  assert.match(generateIndexMarkdown(map), /mobile\.home\.home/);
  const home = resolveFeature(map, 'launch');
  assert.equal(home.id, 'mobile.home.home');
  assert.equal(resolveDriveFlow(home, 'launch'), 'src/QueenZone.Mobile/maestro/flows/01-launch.yaml');
});

test('checkFeatureMap fails when a page is unmapped', () => {
  const root = fixtureRoot();
  write(root, 'src/QueenZone.Web/Pages/About.cshtml', '@page "/about"\n');
  const result = checkFeatureMap({ root, write: true });
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((item) => item.includes('Pages/About.cshtml')));
});

test('checkFeatureMap reports metadata and every referenced resource failure', () => {
  const root = fixtureRoot();
  const mapFile = path.join(root, 'docs/feature-map/mobile/home.json');
  const doc = JSON.parse(readFileSync(mapFile, 'utf8'));
  Object.assign(doc.entries[0], {
    name: '', aliases: null, entry: '', signIn: 'invalid', screen: '',
    sources: ['missing-source'], flows: ['missing-flow'], specs: ['missing-spec'],
    recipe: 'missing-recipe', drive: { smoke: 'missing-drive' },
    testIds: ['missingKey'],
  });
  writeFileSync(mapFile, JSON.stringify(doc));
  const { ok, errors } = checkFeatureMap({ root, write: true });
  assert.equal(ok, false);
  for (const message of [
    'missing name.', 'aliases must be an array.', 'missing entry (user path).',
    'signIn must be none|member|admin.', 'mobile entries need screen.',
    'source missing missing-source.', 'flow missing missing-flow.',
    'spec missing missing-spec.', 'recipe missing missing-recipe.',
    'drive.smoke missing missing-drive.', "testIds key 'missingKey' is missing",
    'Unmapped screen registration HomeStack/Home',
  ]) {
    assert.ok(errors.some((error) => error.includes(message)), message);
  }
});

function changeWebHome(root, changes) {
  const file = path.join(root, 'docs/feature-map/web/home.json');
  const doc = JSON.parse(readFileSync(file, 'utf8'));
  Object.assign(doc.entries[0], changes);
  writeFileSync(file, JSON.stringify(doc));
}

test('web identifiers resolve in page sources and shared partials with either quote style', () => {
  const root = fixtureRoot();
  write(root, 'src/QueenZone.Web/Pages/Shared/_Banner.cshtml', '<div data-testid = \'env-banner\'></div>');
  write(root, 'src/QueenZone.Web/Pages/_Video.cshtml', '<span data-testid="forum-video-card"></span>');
  changeWebHome(root, {
    sources: ['src/QueenZone.Web/Pages/Index.cshtml', 'src/QueenZone.Web/Pages/_Video.cshtml'],
    selectors: ['#qz-hero-archive', '#qz-mobile-menu', '[data-testid="env-banner"]', "[data-testid='forum-video-card']"],
  });
  const result = checkFeatureMap({ root, write: true });
  assert.equal(result.ok, true, result.errors.join('\n'));
});

test('web validation rejects missing identifiers, unsupported selectors and attribute lookalikes', () => {
  const root = fixtureRoot();
  write(root, 'src/QueenZone.Web/Pages/Index.cshtml', '@page "/"\n<div data-id="lookalike" x-data-testid="fake"></div>');
  changeWebHome(root, {
    selectors: ['#missing', '#lookalike', '[data-testid="fake"]', '.css-class', '#wild.card', '[data-testid=""]', null],
  });
  const { errors } = checkFeatureMap({ root, write: true });
  for (const selector of ['#missing', '#lookalike', '[data-testid="fake"]']) {
    assert.ok(errors.some((error) => error.includes(`selector '${selector}' was not found`)));
  }
  for (const selector of ['.css-class', '#wild.card', '[data-testid=""]', null]) {
    assert.ok(errors.some((error) => error.includes(`selector '${selector}' must be #id or [data-testid="value"].`)));
  }
});

test('empty web selectors need a reason and mobile-only fields are rejected', () => {
  const root = fixtureRoot();
  changeWebHome(root, { selectors: [], selectorsReason: ' ', testIds: ['notAMobileKey'], flows: [] });
  const { errors } = checkFeatureMap({ root, write: true });
  assert.ok(errors.some((error) => error.includes('empty web selectors require selectorsReason')));
  assert.ok(errors.some((error) => error.includes('testIds is mobile-only')));
  assert.ok(errors.some((error) => error.includes('flows is mobile-only')));
  assert.ok(!errors.some((error) => error.includes("testIds key 'notAMobileKey'")));
  changeWebHome(root, { selectorsReason: 'Mapping gap: identify a stable locator for the interactive control.' });
  const file = path.join(root, 'docs/feature-map/web/home.json');
  const doc = JSON.parse(readFileSync(file, 'utf8'));
  delete doc.entries[0].testIds;
  delete doc.entries[0].flows;
  writeFileSync(file, JSON.stringify(doc));
  assert.equal(checkFeatureMap({ root, write: true }).ok, true);
});

test('web selectors must be arrays even with a reason', () => {
  const root = fixtureRoot();
  changeWebHome(root, { selectors: '#qz-hero-archive', selectorsReason: 'Role locator.' });
  assert.ok(checkFeatureMap({ root, write: true }).errors.some((error) => error.includes('web selectors must be an array')));
});

test('main --resolve prints a real repo entry', () => {
  const lines = [];
  const code = main(['--resolve', 'mobile.photos.viewer'], {
    stdout: (line) => lines.push(String(line)),
    stderr: () => {},
  });
  assert.equal(code, 0);
  assert.match(lines.join('\n'), /PhotoViewerScreen\.tsx/);
  assert.match(lines.join('\n'), /ZoomableArchiveImage\.tsx/);
  assert.match(lines.join('\n'), /05-photography\.yaml/);
});
