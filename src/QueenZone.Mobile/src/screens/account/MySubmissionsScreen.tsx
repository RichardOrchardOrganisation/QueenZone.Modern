import { useCallback, useEffect, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getAppConfig } from '../../config/appConfig';
import {
  memberAuthHeaders,
  parseArticleSubmissions,
  parseFanPerformanceSubmissions,
  parseNewsSuggestions,
  parsePhotoSubmissions,
  readProblemDetail,
  resolveMediaUrl,
  submissionsApiUrl,
  type ArticleSubmissionItem,
  type FanPerformanceSubmissionItem,
  type NewsSuggestionItem,
  type PhotoSubmissionItem,
  type SubmissionKind,
  type SubmissionStatusTone,
} from '../../api/submissions';
import { usePullToRefresh } from '../../hooks/usePullToRefresh';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { radius, space, type, useTheme, type ColorScheme } from '../../theme';
import { testIds } from '../../test/testIds';
import { ArchiveImage } from '../../ui/ArchiveImage';
import { ThemedRefreshControl } from '../../ui/ThemedRefreshControl';

const kinds: { value: SubmissionKind; label: string }[] = [
  { value: 'photos', label: 'Photos' },
  { value: 'news', label: 'News suggestions' },
  { value: 'articles', label: 'Articles' },
  { value: 'fan-performances', label: 'Fan performances' },
];

export function MySubmissionsScreen() {
  return (
    <MemberGate title="My submissions">
      <MySubmissionsList />
    </MemberGate>
  );
}

function MySubmissionsList() {
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const { accessToken } = useSession();
  const { apiBaseUrl } = getAppConfig();
  const [kind, setKind] = useState<SubmissionKind>('photos');
  const [photos, setPhotos] = useState<PhotoSubmissionItem[]>([]);
  const [news, setNews] = useState<NewsSuggestionItem[]>([]);
  const [articles, setArticles] = useState<ArticleSubmissionItem[]>([]);
  const [performances, setPerformances] = useState<FanPerformanceSubmissionItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  const load = useCallback(async () => {
    if (!accessToken) {
      setPhotos([]);
      setNews([]);
      setArticles([]);
      setPerformances([]);
      setError(
        'Live submission status needs a QueenZone member token. After a real sign-in, pull to refresh to see pending, approved, and rejected items.',
      );
      setLoaded(true);
      return;
    }

    setError(null);
    try {
      const headers = memberAuthHeaders(accessToken);
      const [photoRes, newsRes, articleRes, performanceRes] = await Promise.all([
        fetch(submissionsApiUrl(apiBaseUrl, 'photos'), { headers }),
        fetch(submissionsApiUrl(apiBaseUrl, 'news'), { headers }),
        fetch(submissionsApiUrl(apiBaseUrl, 'articles'), { headers }),
        fetch(submissionsApiUrl(apiBaseUrl, 'fan-performances'), { headers }),
      ]);

      const [photoPayload, newsPayload, articlePayload, performancePayload] = await Promise.all([
        photoRes.json().catch(() => null),
        newsRes.json().catch(() => null),
        articleRes.json().catch(() => null),
        performanceRes.json().catch(() => null),
      ]);

      if (!photoRes.ok) {
        throw new Error(readProblemDetail(photoPayload, 'Could not load your submissions.'));
      }

      if (!newsRes.ok) {
        throw new Error(readProblemDetail(newsPayload, 'Could not load your submissions.'));
      }

      if (!articleRes.ok) {
        throw new Error(readProblemDetail(articlePayload, 'Could not load your submissions.'));
      }

      if (!performanceRes.ok) {
        throw new Error(readProblemDetail(performancePayload, 'Could not load your submissions.'));
      }

      setPhotos(parsePhotoSubmissions(photoPayload).items);
      setNews(parseNewsSuggestions(newsPayload).items);
      setArticles(parseArticleSubmissions(articlePayload).items);
      setPerformances(parseFanPerformanceSubmissions(performancePayload).items);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not load your submissions.');
    } finally {
      setLoaded(true);
    }
  }, [accessToken, apiBaseUrl]);

  useEffect(() => {
    void load();
  }, [load]);

  const pull = usePullToRefresh([load]);

  const emptyCopy =
    kind === 'photos'
      ? 'You have not submitted any photos yet.'
      : kind === 'news'
        ? 'You have not suggested any news yet.'
        : kind === 'articles'
          ? 'You have not submitted any articles yet.'
          : 'You have not submitted any fan performances yet.';

  const isEmpty =
    loaded
    && !error
    && ((kind === 'photos' && photos.length === 0)
      || (kind === 'news' && news.length === 0)
      || (kind === 'articles' && articles.length === 0)
      || (kind === 'fan-performances' && performances.length === 0));

  return (
    <ScrollView
      testID={testIds.mySubmissionsScreen}
      style={[styles.flex, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={[styles.content, { paddingBottom: insets.bottom + space.xxl }]}
      alwaysBounceVertical
      refreshControl={
        <ThemedRefreshControl refreshing={pull.refreshing} onRefresh={pull.onRefresh} />
      }
    >
      <Text style={[type.eyebrow, { color: c.accentPrimary }]}>Members</Text>
      <Text style={[type.pageTitle, { color: c.textPrimary }]} maxFontSizeMultiplier={1.4} allowFontScaling>
        My submissions
      </Text>
      <Text style={[type.body, { color: c.textSecondary }]} allowFontScaling>
        Track photos, news suggestions, articles, and fan performances you have submitted. Pull to refresh after an admin reviews them.
      </Text>

      <View style={styles.tabs} accessibilityRole="tablist">
        {kinds.map((item) => {
          const selected = item.value === kind;
          return (
            <Pressable
              key={item.value}
              testID={`${testIds.mySubmissionsTabPrefix}${item.value}`}
              accessibilityRole="tab"
              accessibilityState={{ selected }}
              accessibilityLabel={item.label}
              onPress={() => setKind(item.value)}
              style={({ pressed }) => [
                styles.tab,
                {
                  backgroundColor: selected ? c.accentTintWeak : c.surfaceCard,
                  borderColor: selected ? c.accentPrimary : c.border,
                },
                pressed && styles.pressed,
              ]}
            >
              <Text style={[type.caption, { color: selected ? c.accentPrimary : c.textSecondary }]}>
                {item.label}
              </Text>
            </Pressable>
          );
        })}
      </View>

      {error ? (
        <View style={[styles.notice, { borderColor: c.danger }]}>
          <Text style={[type.body, { color: c.danger }]}>{error}</Text>
          <Pressable accessibilityRole="button" accessibilityLabel="Retry loading submissions" onPress={() => void load()}>
            <Text style={[type.button, { color: c.accentPrimary, marginTop: space.sm }]}>Retry</Text>
          </Pressable>
        </View>
      ) : null}

      {isEmpty ? (
        <Text style={[type.body, { color: c.textSecondary }]}>{emptyCopy}</Text>
      ) : null}

      {kind === 'photos'
        ? photos.map((item) => {
            const thumbUri = resolveMediaUrl(apiBaseUrl, item.thumbnailPath);
            return (
              <View key={item.id} style={[styles.card, { borderColor: c.border, backgroundColor: c.surfaceCard }]}>
                <View style={styles.photoRow}>
                  {thumbUri ? (
                    <ArchiveImage
                      source={{ uri: thumbUri }}
                      style={styles.thumb}
                      priority="low"
                      recyclingKey={item.id}
                      label={item.title}
                      accessibilityIgnoresInvertColors
                    />
                  ) : (
                    <View style={[styles.thumb, { backgroundColor: c.surfaceRaised }]} />
                  )}
                  <View style={styles.cardBody}>
                    <Text style={[type.cardTitle, { color: c.textPrimary }]}>{item.title}</Text>
                    <StatusBadge tone={item.status.statusTone} label={item.status.statusLabel} />
                    <Text style={[type.caption, { color: c.textMuted }]}>{formatWhen(item.submittedAt)}</Text>
                    {item.notes ? (
                      <Text style={[type.body, { color: c.textSecondary }]}>{item.notes}</Text>
                    ) : null}
                  </View>
                </View>
              </View>
            );
          })
        : null}

      {kind === 'news'
        ? news.map((item) => (
            <View key={item.id} style={[styles.card, { borderColor: c.border, backgroundColor: c.surfaceCard }]}>
              <Text style={[type.cardTitle, { color: c.textPrimary }]}>{item.title ?? item.truncatedUrl}</Text>
              <StatusBadge tone={item.status.statusTone} label={item.status.statusLabel} />
              <Text style={[type.caption, { color: c.textMuted }]}>{item.truncatedUrl}</Text>
              <Text style={[type.caption, { color: c.textMuted }]}>{formatWhen(item.submittedAt)}</Text>
              {item.notes ? <Text style={[type.body, { color: c.textSecondary }]}>{item.notes}</Text> : null}
              {item.publishedPath ? (
                <Text style={[type.caption, { color: c.accentPrimary }]}>Published on the website</Text>
              ) : null}
            </View>
          ))
        : null}

      {kind === 'articles'
        ? articles.map((item) => (
            <View key={item.id} style={[styles.card, { borderColor: c.border, backgroundColor: c.surfaceCard }]}>
              <Text style={[type.cardTitle, { color: c.textPrimary }]}>{item.title}</Text>
              <StatusBadge tone={item.status.statusTone} label={item.status.statusLabel} />
              <Text style={[type.caption, { color: c.textMuted }]}>
                {item.submittedAt ? formatWhen(item.submittedAt) : 'Draft'}
              </Text>
              {item.notes ? <Text style={[type.body, { color: c.textSecondary }]}>{item.notes}</Text> : null}
              {item.canContinueEditing ? (
                <Text style={[type.caption, { color: c.accentPrimary }]}>Continue editing on the website</Text>
              ) : null}
              {item.publishedPath ? (
                <Text style={[type.caption, { color: c.accentPrimary }]}>Published on the website</Text>
              ) : null}
            </View>
          ))
        : null}

      {kind === 'fan-performances'
        ? performances.map((item) => (
            <View key={item.id} style={[styles.card, { borderColor: c.border, backgroundColor: c.surfaceCard }]}>
              <Text style={[type.cardTitle, { color: c.textPrimary }]}>{item.title}</Text>
              <StatusBadge tone={item.status.statusTone} label={item.status.statusLabel} />
              <Text style={[type.caption, { color: c.textMuted }]}>
                {item.coveredSong} · {item.performedBy}
              </Text>
              <Text style={[type.caption, { color: c.textMuted }]}>{formatWhen(item.submittedAt)}</Text>
              {item.notes ? <Text style={[type.body, { color: c.textSecondary }]}>{item.notes}</Text> : null}
              {item.publishedPath ? (
                <Text style={[type.caption, { color: c.accentPrimary }]}>Published on the fan stage</Text>
              ) : null}
            </View>
          ))
        : null}
    </ScrollView>
  );
}

function StatusBadge({ tone, label }: { tone: SubmissionStatusTone; label: string }) {
  const { c } = useTheme();
  const colors = badgeColors(tone, c);
  return (
    <View style={[styles.badge, { borderColor: colors.border, backgroundColor: colors.background }]}>
      <Text style={[type.meta, { color: colors.text }]}>{label}</Text>
    </View>
  );
}

function badgeColors(
  tone: SubmissionStatusTone,
  c: ColorScheme,
): { background: string; border: string; text: string } {
  switch (tone) {
    case 'success':
      return { background: c.successTint, border: c.successBorder, text: c.success };
    case 'danger':
      return { background: c.accentTintWeak, border: c.danger, text: c.danger };
    case 'attention':
      return { background: c.accentTintWeak, border: c.accentPrimary, text: c.accentPrimary };
    case 'review':
      return { background: c.accentTintWeak, border: c.accentPrimary, text: c.accentPrimary };
    default:
      return { background: c.surfaceRaised, border: c.border, text: c.textSecondary };
  }
}

function formatWhen(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toISOString().replace('.000Z', 'Z');
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.base,
    gap: space.md,
  },
  tabs: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.sm,
  },
  tab: {
    borderWidth: 1,
    borderRadius: radius.pill,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
    minHeight: 40,
    justifyContent: 'center',
  },
  card: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.base,
    gap: space.sm,
  },
  photoRow: {
    flexDirection: 'row',
    gap: space.md,
  },
  thumb: {
    width: space.thumb,
    height: space.thumb,
    borderRadius: radius.xs,
  },
  cardBody: {
    flex: 1,
    gap: space.xs,
  },
  badge: {
    alignSelf: 'flex-start',
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: space.sm,
    paddingVertical: 2,
  },
  notice: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.base,
    gap: space.sm,
  },
  pressed: {
    opacity: 0.85,
  },
});
