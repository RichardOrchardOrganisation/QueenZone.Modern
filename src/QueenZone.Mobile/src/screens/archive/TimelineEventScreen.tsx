import { useCallback, useEffect, useLayoutEffect } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { ApiError, fetchTimelineEventById } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import { HeaderBackButton } from '../../navigation/headerButtons';
import { goBackOrFallback } from '../../navigation/nestedTab';
import type { ArchiveStackParamList } from '../../navigation/types';
import { openExternalUrl } from '../../ui/openExternalUrl';
import { LoadingBlock } from '../../ui/ScreenStates';
import { Button } from '../../ui/Button';
import { testIds } from '../../test/testIds';
import { space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'TimelineEvent'>;

export function TimelineEventScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { id } = route.params;
  const loadEvent = useCallback(
    (signal: AbortSignal) => {
      if (!Number.isInteger(id) || id <= 0) {
        return Promise.reject(new ApiError(404, 'Not Found'));
      }
      return fetchTimelineEventById(id, signal);
    },
    [id],
  );
  const { data: event, error, loading } = useDetailQuery(loadEvent);

  useLayoutEffect(() => {
    navigation.setOptions({
      title: 'On This Day',
      headerLeft: () => (
        <HeaderBackButton
          testID={testIds.timelineEventBack}
          onPress={() => goBackOrFallback(navigation, 'Timeline')}
        />
      ),
    });
  }, [navigation]);

  useEffect(() => {
    if (!Number.isInteger(id) || id <= 0) {
      navigation.replace('Timeline');
      return;
    }
    if (!loading && (error || !event)) {
      navigation.replace('Timeline');
    }
  }, [error, event, id, loading, navigation]);

  if (loading || error || !event) {
    return <LoadingBlock label="Loading timeline event…" />;
  }

  return (
    <ScrollView
      testID={testIds.timelineEventScreen}
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <Text style={[type.eyebrow, { color: c.accentArchive }]}>On This Day</Text>
      <Text style={[type.meta, { color: c.textMuted, marginTop: space.sm }]}>{event.formattedDate}</Text>
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.sm }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {event.title}
      </Text>
      <Text style={[type.meta, { color: c.accentArchive, marginTop: space.md }]}>{event.categoryLabel}</Text>
      <Text style={[type.body, { color: c.textSecondary, marginTop: space.md }]}>{event.summary}</Text>
      {event.sourceUrl ? (
        <Text
          testID={testIds.timelineEventSource}
          accessibilityRole="link"
          onPress={() => void openExternalUrl(event.sourceUrl!)}
          style={[type.button, { color: c.accentPrimary, marginTop: space.lg }]}
        >
          Source
        </Text>
      ) : null}
      <View style={styles.cta}>
        <Button
          testID={testIds.timelineEventSeeAll}
          label="See all"
          variant="outline"
          onPress={() => navigation.navigate('Timeline')}
        />
      </View>
      <View style={{ height: space.section }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: {
    paddingHorizontal: 26,
    paddingTop: space.xl,
    paddingBottom: space.section,
  },
  cta: {
    marginTop: space.xxl,
  },
});
