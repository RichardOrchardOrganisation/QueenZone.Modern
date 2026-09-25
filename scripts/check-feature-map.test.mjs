import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  checkFeatureMap,
  generateIndexMarkdown,
  loadFeatureMap,
  main,
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
