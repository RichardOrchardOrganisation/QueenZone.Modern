import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  checkFeatureMap,
  PENDING_ADMIN_CEILING,
  CONFIRM_ONLY_ADMIN_PAGES,
  GET_WRITES_ADMIN_PAGES,
  pageModelPostHandlers,
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
  for (const area of ['news', 'photos', 'archive', 'forum', 'account', 'messages', 'static', 'admin']) {
    write(root, `docs/feature-map/web/${area}.json`, JSON.stringify({ area, surface: 'web', entries: [] }, null, 2));
  }
  write(root, 'docs/feature-map/excluded.json', JSON.stringify({ excluded: [] }));
  write(root, 'docs/feature-map/pending-admin.json', JSON.stringify({ pendingAdminAreas: ['Pages/Admin/Index.cshtml'] }));
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

test('web-only field validation rejects present fields even when empty or null', () => {
  const root = fixtureRoot();
  changeWebHome(root, { testIds: null, flows: undefined });
  const { errors } = checkFeatureMap({ root, write: true });
  assert.ok(errors.includes('web.home.index: testIds is mobile-only; web entries use selectors and specs.'));
  assert.ok(!errors.includes('web.home.index: flows is mobile-only; web entries use selectors and specs.'));
});

function pendingAreas(root, patterns) {
  write(root, 'docs/feature-map/pending-admin.json', JSON.stringify({ pendingAdminAreas: patterns }));
}

function adminFixture(page = 'Pages/Admin/Index.cshtml', changes = {}) {
  const root = fixtureRoot();
  pendingAreas(root, page === 'Pages/Admin/Index.cshtml' ? [] : ['Pages/Admin/Index.cshtml']);
  write(root, `src/QueenZone.Web/${page}`, '@page "/admin/test"\n@model TestModel\n');
  write(root, 'tests/AdminTests.cs', '// fixture spec\n');
  const entry = {
    id: 'web.admin.test', name: 'Admin', aliases: [], page, url: '/admin/test',
    entry: 'Open admin', sources: [`src/QueenZone.Web/${page}`],
    selectors: [], selectorsReason: 'Role locators.', specs: ['tests/AdminTests.cs'],
    signIn: 'admin', kind: 'handler',
    admin: { actions: [], verification: 'none', externalEffects: [], getWrites: false },
    ...changes,
  };
  write(root, 'docs/feature-map/web/admin.json', JSON.stringify({ area: 'admin', surface: 'web', entries: [entry] }));
  return root;
}

function checked(root) {
  return checkFeatureMap({ root, write: true });
}

for (const absent of [false, true]) {
  test(`empty exclusions allowed (absent: ${absent})`, () => {
    const root = fixtureRoot();
    if (absent) rmSync(path.join(root, 'docs/feature-map/excluded.json'));
    const result = checked(root);
    assert.equal(result.ok, true, result.errors.join('\n'));
    assert.match(result.index, /## Excluded\n\n_None\._/);
    assert.match(result.index, /## Pending admin areas \(ceiling 14\)/);
    assert.match(result.index, /### admin/);
  });
}

test('non-admin exclusions still require pattern and reason, but allow multiple blocks', () => {
  const root = fixtureRoot();
  const excluded = [{ pattern: 'Pages/About.cshtml', reason: 'Example' }, { pattern: 'Pages/Other.cshtml', reason: 'Example' }];
  write(root, 'docs/feature-map/excluded.json', JSON.stringify({ excluded }));
  assert.equal(checked(root).ok, true);
  excluded.push({ pattern: 'Pages/Missing.cshtml' });
  write(root, 'docs/feature-map/excluded.json', JSON.stringify({ excluded }));
  assert.ok(checked(root).errors.some((error) => error.includes('pattern and reason')));
});

test('Admin exclusions are rejected', () => {
  const root = fixtureRoot();
  write(root, 'docs/feature-map/excluded.json', JSON.stringify({ excluded: [{ pattern: 'Pages/Admin/**', reason: 'Legacy' }] }));
  assert.ok(checked(root).errors.includes('Admin pages may not be excluded; map them or list the area in pendingAdminAreas'));
});

for (const [patterns, message] of [
  [Array(PENDING_ADMIN_CEILING + 1).fill('Pages/Admin/Index.cshtml'), 'exceeds ceiling'],
  [['Pages/Admin/Index.cshtml', 'Pages/Admin/Index.cshtml'], 'Duplicate pending'],
  [['Pages/Admin/Missing/**'], 'matches no page files'],
  [['Pages/Index.cshtml'], 'must be under Pages/Admin/'],
  [['Pages/Admin/../Index.cshtml'], 'must be under Pages/Admin/'],
  [null, 'must be an array'],
]) {
  test(`pending validation: ${message} (${JSON.stringify(patterns)})`, () => {
    const root = fixtureRoot();
    pendingAreas(root, patterns);
    assert.ok(checked(root).errors.some((error) => error.includes(message)));
  });
}

test('pending patterns cannot overlap mapped pages', () => {
  const root = adminFixture();
  pendingAreas(root, ['Pages/Admin/**']);
  assert.ok(checked(root).errors.some((error) => error.includes('matches mapped page')));
});

test('absent pending list is empty and new admin folders fail immediately', () => {
  const root = adminFixture();
  rmSync(path.join(root, 'docs/feature-map/pending-admin.json'));
  assert.equal(checked(root).ok, true);
  write(root, 'src/QueenZone.Web/Pages/Admin/NewArea/Index.cshtml', '@page\n');
  assert.ok(checked(root).errors.includes('Unmapped Razor page Pages/Admin/NewArea/Index.cshtml.'));
});

test('pending patterns ignore partials and views without @page', () => {
  const root = fixtureRoot();
  write(root, 'src/QueenZone.Web/Pages/Admin/OnlyViews/_Partial.cshtml', '@page\n');
  write(root, 'src/QueenZone.Web/Pages/Admin/OnlyViews/View.cshtml', '<p>View</p>');
  pendingAreas(root, ['Pages/Admin/Index.cshtml', 'Pages/Admin/OnlyViews/**']);
  assert.ok(checked(root).errors.some((error) => error.includes('matches no page files')));
});

for (const [changes, message] of [
  [{ admin: undefined }, 'require an admin object'],
  [{ signIn: 'member' }, 'require signIn admin'],
  [{ specs: [] }, 'require at least one spec'],
  [{ admin: { actions: [1], externalEffects: 'email', verification: 'bad', getWrites: 'false', notes: 1 } }, 'admin.actions must be a string array'],
]) {
  test(`admin schema: ${message}`, () => {
    assert.ok(checked(adminFixture(undefined, changes)).errors.some((error) => error.includes(message)));
  });
}

test('admin field types are all checked and non-admin signIn admin stays lenient', () => {
  const result = checked(adminFixture(undefined, { admin: { actions: [], externalEffects: [1], verification: false, getWrites: null, notes: [] } }));
  for (const field of ['externalEffects', 'verification', 'getWrites', 'notes']) {
    assert.ok(result.errors.some((error) => error.includes(`admin.${field}`)));
  }
  const root = fixtureRoot();
  changeWebHome(root, { signIn: 'admin' });
  assert.equal(checked(root).ok, true);
});

test('OnPost handlers forbid none and require normalized actions', () => {
  const root = adminFixture();
  write(root, 'src/QueenZone.Web/Pages/Admin/Index.cshtml.cs', 'class TestModel { void OnPost() {} void OnPostAsync() {} void OnPostSaveAsync() { if (true) {} } } class Other { void OnPostDelete() {} }');
  assert.deepEqual(pageModelPostHandlers(root, 'Pages/Admin/Index.cshtml'), ['OnPost', 'OnPostAsync', 'OnPostSaveAsync']);
  const result = checked(root);
  assert.ok(result.errors.some((error) => error.includes('verification none')));
  assert.ok(result.errors.some((error) => error.includes('missing handler Post')));
  assert.ok(result.errors.some((error) => error.includes('missing handler Save')));
  const file = path.join(root, 'docs/feature-map/web/admin.json');
  const doc = JSON.parse(readFileSync(file, 'utf8'));
  Object.assign(doc.entries[0].admin, { actions: ['Post', 'Save'], verification: 'fixture-record' });
  writeFileSync(file, JSON.stringify(doc));
  assert.equal(checked(root).ok, true);
});

test('model discovery supports namespace, generics, local and Admin-tree models', () => {
  const root = adminFixture('Pages/Admin/Area/Test.cshtml');
  write(root, 'src/QueenZone.Web/Pages/Admin/Area/Test.cshtml', '@page\n@model Example.SharedModel<string>\n');
  write(root, 'src/QueenZone.Web/Pages/Admin/Models.cs', 'class SharedModel<T> { void OnPostSaveAsync() {} } class Other { void OnPostWrong() {} }');
  assert.deepEqual(pageModelPostHandlers(root, 'Pages/Admin/Area/Test.cshtml'), ['OnPostSaveAsync']);
  write(root, 'src/QueenZone.Web/Pages/Admin/Area/Models.cs', 'class SharedModel<T> { void OnPostLocal() {} }');
  assert.deepEqual(pageModelPostHandlers(root, 'Pages/Admin/Area/Test.cshtml'), ['OnPostLocal']);
});

test('model discovery handles long blank prefixes without cross-line backtracking', () => {
  const root = adminFixture('Pages/Admin/Area/Test.cshtml');
  write(root, 'src/QueenZone.Web/Pages/Admin/Area/Models.cs', 'class SharedModel<T> { void OnPostSaveAsync() {} } class Other { void OnPostWrong() {} }');
  for (const newline of ['\r\n', '\n', '\u2028', '\u2029']) {
    const prefix = (' '.repeat(2000) + newline).repeat(1000);
    write(root, 'src/QueenZone.Web/Pages/Admin/Area/Test.cshtml', prefix + '@page' + newline + '\t @model\t Example.SharedModel<string>' + newline);
    const started = performance.now();
    assert.deepEqual(pageModelPostHandlers(root, 'Pages/Admin/Area/Test.cshtml'), ['OnPostSaveAsync']);
    assert.ok(performance.now() - started < 500);
  }
});

for (const pattern of Object.keys(CONFIRM_ONLY_ADMIN_PAGES)) {
  test(`confirm-only enforced for ${pattern}`, () => {
    const page = pattern.replace('/**', '/Index.cshtml');
    assert.ok(checked(adminFixture(page)).errors.some((error) => error.includes('requires confirm-only')));
    assert.equal(checked(adminFixture(page, { admin: { actions: [], externalEffects: [], getWrites: false, verification: 'confirm-only' } })).ok, true);
  });
}

for (const page of GET_WRITES_ADMIN_PAGES) {
  test(`GET audit writes enforced for ${page}`, () => {
    assert.ok(checked(adminFixture(page)).errors.some((error) => error.includes('requires getWrites true')));
    assert.equal(checked(adminFixture(page, { admin: { actions: [], externalEffects: [], getWrites: true, verification: 'none' } })).ok, true);
  });
}

test('destructive fixture-record actions warn without failing and main prints to stderr', () => {
  const root = adminFixture(undefined, { admin: { actions: ['Delete'], externalEffects: [], verification: 'fixture-record', getWrites: false } });
  const result = checked(root);
  assert.equal(result.ok, true, result.errors.join('\n'));
  assert.equal(result.warnings.length, 1);
  assert.match(result.warnings[0], /confirm the spec only completes on records it created/);
  const errors = [];
  assert.equal(main([], { root, stdout: () => {}, stderr: (line) => errors.push(line) }), 0);
  assert.match(errors.join('\n'), /Warning:.*confirm the spec/);
});
