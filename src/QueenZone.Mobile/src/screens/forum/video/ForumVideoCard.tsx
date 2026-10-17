import { useEffect, useMemo, useRef, useState } from 'react';
import { AccessibilityInfo, findNodeHandle, Pressable, Text, View, useWindowDimensions, type LayoutChangeEvent } from 'react-native';
import WebView from 'react-native-webview';
import type { YoutubeVideo } from './descriptors';
import { useForumPlayers } from './ForumPlayerProvider';
import { parsePlayerMessage, pauseScript, playerBaseUrl, playerDocument, playerError, playerNavigation, playerTimeoutMs } from './playerDocument';
import { openExternalUrl } from '../../../ui/openExternalUrl';
import { RichHtmlBody } from '../../../ui/RichHtmlBody';
import { space, type, useTheme } from '../../../theme';
import { testIds } from '../../../test/testIds';

type Props = { cardKey: string; video: YoutubeVideo; originalHtml: string };
export function ForumVideoCard({ cardKey, video, originalHtml }: Props) {
  const { c } = useTheme();
  const { fontScale } = useWindowDimensions();
  // Fabric can repaint scaled glyphs without invalidating an unchanged Text
  // layout after a live Dynamic Type change. Recreate only text leaves so the
  // label is remeasured; keep the player, fallback control and focus stable.
  const context = useForumPlayers();
  const view = useRef<View>(null);
  const player = useRef<WebView<object>>(null);
  const fallback = useRef<View>(null);
  const accessibilityFocused = useRef(false);
  const [owner, setOwner] = useState<number | null>(null);
  const [width, setWidth] = useState(0);
  const entry = context?.snapshot[cardKey];
  const generation = entry?.generation ?? null;
  const height = Math.max(200, width * 9 / 16);
  // Bound the longest status at the current text scale; retain the same slot
  // through load/eviction/failure transitions, including accessibility sizes.
  const statusHeight = Math.max(100, Math.ceil(120 * type.caption.fontSize * fontScale / Math.max(width, 200)) * type.caption.lineHeight * fontScale);
  const pool = context?.pool;
  const attach = context?.attach;
  const measure = context?.measure;
  useEffect(() => {
    if (!pool || !attach) return;
    const token = pool.register(cardKey); setOwner(token);
    let alive = true;
    const detach = attach(cardKey, {
      pause: () => player.current?.injectJavaScript(pauseScript),
      measure: (viewportTop, viewportBottom, valid) => view.current?.measureInWindow((_x, top, measuredWidth, measuredHeight) => {
        if (alive && valid()) pool.measure(cardKey, token, { top, bottom: top + measuredHeight, width: measuredWidth, viewportTop, viewportBottom }, Date.now(), true);
      }),
    });
    return () => { alive = false; detach(); pool.remove(cardKey, token); };
  }, [attach, cardKey, pool]);
  const [bootstrap, setBootstrap] = useState<number | null>(null);
  const [ready, setReady] = useState<number | null>(null);
  useEffect(() => {
    if (generation == null || !pool || ready === generation) return;
    // Cold WebKit startup must not consume the provider readiness budget.
    // The fixed document emits bootstrap once before loading YouTube. Each
    // phase is bounded to 12s, so total loading cannot exceed 24s. Repeated
    // same-generation bootstrap messages do not reset this effect.
    const timer = setTimeout(() => {
      pool.fail(cardKey, generation, playerError(bootstrap === generation ? 'timeout' : 'startup'));
    }, playerTimeoutMs);
    return () => clearTimeout(timer);
  }, [bootstrap, cardKey, generation, pool, ready]);
  const wasMounted = useRef(false);
  useEffect(() => {
    if (wasMounted.current && generation == null && accessibilityFocused.current) {
      const target = findNodeHandle(fallback.current);
      if (target != null) AccessibilityInfo.setAccessibilityFocus(target);
      accessibilityFocused.current = false;
    }
    wasMounted.current = generation != null;
  }, [generation]);
  const source = useMemo(() => generation == null ? undefined
    : { html: playerDocument(video, generation), baseUrl: playerBaseUrl }, [generation, video]);
  const onLayout = (event: LayoutChangeEvent) => {
    const measuredWidth = event.nativeEvent.layout.width;
    if (measuredWidth > 0) setWidth(measuredWidth);
    measure?.();
  };
  let status = entry?.failed;
  if (status == null) {
    if (!context?.online) status = 'Offline. Watch on YouTube when connected.';
    else if (width < 200) status = 'Inline video needs more space. Watch on YouTube.';
    else if (generation == null) status = 'Loads from YouTube automatically near the screen.';
    else if (ready === generation) status = 'Ready. Press the YouTube Play control to play.';
    else status = 'Loading YouTube player…';
  }
  return <View style={{ marginVertical: space.md }}>
    <RichHtmlBody html={originalHtml} horizontalInset={space.xl} />
    <Text key={fontScale} style={[type.caption, { color: c.textSecondary }]} allowFontScaling>
      Videos near the screen load automatically from YouTube before Play. Loading shares your IP address and this app’s identification with YouTube.
    </Text>
    <View ref={view} collapsable={false} onLayout={onLayout} style={{ height, width: '100%', backgroundColor: c.surfacePage }}>
      {generation != null && source && width >= 200 ? <WebView<object>
        key={generation} ref={player} testID={testIds.forumYoutubePlayer}
        accessibilityLabel="YouTube video player" onFocus={() => { accessibilityFocused.current = true; }}
        source={source} style={{ height, width: '100%' }}
        javaScriptEnabled domStorageEnabled={false} incognito sharedCookiesEnabled={false} thirdPartyCookiesEnabled={false}
        cacheEnabled={false} allowFileAccess={false} allowFileAccessFromFileURLs={false}
        allowUniversalAccessFromFileURLs={false} allowingReadAccessToURL={undefined}
        mixedContentMode="never" javaScriptCanOpenWindowsAutomatically={false} setSupportMultipleWindows
        mediaPlaybackRequiresUserAction allowsInlineMediaPlayback allowsFullscreenVideo
        allowsAirPlayForMediaPlayback={false} allowsPictureInPictureMediaPlayback={false}
        scrollEnabled={false} bounces={false} allowsBackForwardNavigationGestures={false}
        originWhitelist={['*']} webviewDebuggingEnabled={false}
        onShouldStartLoadWithRequest={request => {
          const decision = playerNavigation(request.url, request.isTopFrame, request.navigationType);
          if (decision === 'external') void openExternalUrl(request.url);
          return decision === 'allow';
        }}
        onOpenWindow={event => {
          const url = event.nativeEvent.targetUrl;
          // CNG only forwards OS-identified user link activations. No popup
          // WebView is created, and absent hit-test URLs keep the fallback link.
          if (playerNavigation(url, true, 'click') === 'external') void openExternalUrl(url);
        }}
        onFileDownload={() => { /* Downloads are not a player capability. */ }}
        onMessage={event => {
          const message = parsePlayerMessage(event.nativeEvent.data, generation);
          if (!message || pool?.cards[cardKey]?.generation !== generation) return;
          if (message.event === 'bootstrap') setBootstrap(generation);
          else if (message.event === 'ready') setReady(generation);
          else if (message.event === 'error') pool.fail(cardKey, generation, playerError(message.value));
          else pool.message(cardKey, generation, message.event, message.value);
        }}
        onError={() => pool?.fail(cardKey, generation, playerError('load'))}
        onHttpError={() => pool?.fail(cardKey, generation, playerError('http'))}
        onContentProcessDidTerminate={() => pool?.fail(cardKey, generation, playerError('process'))}
        onRenderProcessGone={() => pool?.fail(cardKey, generation, playerError('process'))}
      /> : null}
    </View>
    <View style={{ minHeight: statusHeight }}><Text key={fontScale} testID={testIds.forumYoutubeStatus} style={[type.caption, { color: c.textSecondary }]} allowFontScaling>{status}</Text></View>
    <Pressable accessibilityRole="button" accessibilityLabel="Retry YouTube video" disabled={!entry?.failed || !context?.online}
      accessibilityElementsHidden={!entry?.failed} importantForAccessibility={entry?.failed ? 'auto' : 'no-hide-descendants'} style={{ opacity: entry?.failed ? 1 : 0 }}
      testID={testIds.forumYoutubeRetry} onPress={() => { if (owner != null) pool?.retry(cardKey, owner); }}>
      <Text key={fontScale} style={[type.body, { color: c.accentPrimary }]}>Retry</Text>
    </Pressable>
    <Pressable ref={fallback} accessibilityRole="link" accessibilityLabel="Watch on YouTube" testID={testIds.forumYoutubeWatch}
      onPress={() => { void openExternalUrl(video.watchUrl); }}>
      <Text key={fontScale} style={[type.body, { color: c.accentPrimary }]}>Watch on YouTube</Text>
    </Pressable>
  </View>;
}
