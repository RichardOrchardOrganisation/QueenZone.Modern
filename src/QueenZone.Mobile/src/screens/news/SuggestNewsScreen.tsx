import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useCallback, type ReactNode } from 'react';
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import type { HomeStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { openSignIn } from '../../session/signInNavigation';
import { getNewsShareController, useNewsShare } from '../../share/news/NewsShare';
import { hostOf } from '../../share/news/parseShare';
import { testIds } from '../../test/testIds';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { FormFieldLabel, FormScreenLayout } from '../../ui/FormScreenLayout';

type Props = NativeStackScreenProps<HomeStackParamList, 'SuggestNews'>;

const titleMax = 300;
const notesMax = 1000;

export function SuggestNewsScreen({ navigation }: Props) {
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const session = useSession();
  const share = useNewsShare();

  const goBackOrHome = useCallback(() => {
    if (navigation.canGoBack()) {
      navigation.goBack();
      return;
    }
    navigation.navigate('Home');
  }, [navigation]);

  if (share.kind === 'choose') {
    return (
      <ScreenShell insetsBottom={insets.bottom} background={c.surfacePage}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>News</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]}>Which link?</Text>
        <Text style={[type.body, { color: c.textSecondary }]}>
          That share contained more than one web link. Pick the https:// story you want to suggest.
        </Text>
        <View testID={testIds.suggestNewsChooser} style={styles.fields}>
          {share.candidates.map((url) => {
            const https = url.startsWith('https://');
            return (
              <Pressable
                key={url}
                accessibilityRole="button"
                accessibilityLabel={https ? `Use ${url}` : `${url} is not https`}
                onPress={() => share.choose(url)}
                style={[styles.candidate, { borderColor: https ? c.border : c.danger, backgroundColor: c.surfaceCard }]}
              >
                <Text style={[type.caption, { color: c.textMuted }]}>{hostOf(url) || 'link'}</Text>
                <Text style={[type.body, { color: c.textPrimary }]}>{url}</Text>
                {!https ? (
                  <Text style={[type.caption, { color: c.danger }]}>Needs an https:// link</Text>
                ) : null}
              </Pressable>
            );
          })}
        </View>
        <Button
          label="Cancel"
          variant="outline"
          testID={testIds.suggestNewsCancel}
          onPress={() => {
            share.cancel();
            goBackOrHome();
          }}
        />
      </ScreenShell>
    );
  }

  if (share.kind === 'rejected') {
    return (
      <ScreenShell insetsBottom={insets.bottom} background={c.surfacePage}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>News</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]}>Could not use that share</Text>
        <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
          {share.detail}
        </Text>
        <Button
          label="Dismiss"
          variant="outline"
          testID={testIds.suggestNewsCancel}
          onPress={() => {
            share.cancel();
            goBackOrHome();
          }}
        />
      </ScreenShell>
    );
  }

  if (share.kind === 'submitted') {
    return (
      <ScreenShell insetsBottom={insets.bottom} background={c.surfacePage}>
        <View testID={testIds.suggestNewsSuccess} style={styles.fields}>
          <Text style={[type.eyebrow, { color: c.accentPrimary }]}>News</Text>
          <Text style={[type.pageTitle, { color: c.textPrimary }]}>Suggestion sent</Text>
          <Text style={[type.body, { color: c.textSecondary }]} accessibilityRole="alert">
            Thanks. We will review this story.
          </Text>
          <Button
            label="View my submissions"
            testID={testIds.suggestNewsViewSubmissions}
            onPress={() => {
              share.acknowledge();
              navigation.navigate('MySubmissions');
            }}
          />
        </View>
      </ScreenShell>
    );
  }

  if (share.kind === 'idle') {
    return (
      <ScreenShell insetsBottom={insets.bottom} background={c.surfacePage} testID={testIds.suggestNewsScreen}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>News</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]}>Suggest news</Text>
        <Text style={[type.body, { color: c.textSecondary }]}>No story is waiting to be suggested.</Text>
        <Button label="Close" variant="outline" testID={testIds.suggestNewsCancel} onPress={goBackOrHome} />
      </ScreenShell>
    );
  }

  const draft = share.draft;
  const busy = share.kind === 'submitting';
  const error = share.kind === 'failed' ? share.error : null;
  const host = hostOf(draft.url);
  const canPatch = share.kind === 'form' || share.kind === 'failed';
  const patch = canPatch ? share.patch : () => undefined;
  const canSubmit = session.isSignedIn && !busy && draft.url.trim().length > 0;

  return (
    <FormScreenLayout testID={testIds.suggestNewsScreen} backgroundColor={c.surfacePage} bottomInset={insets.bottom}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>News</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]} maxFontSizeMultiplier={1.4} allowFontScaling>
          Suggest news
        </Text>
        <Text style={[type.body, { color: c.textSecondary }]} allowFontScaling>
          Review the public https:// link, add an optional headline, and send it to the editors. We do not fetch the
          page from this device.
        </Text>

        <View style={styles.fields}>
          {host ? <Text style={[type.caption, { color: c.accentPrimary }]}>{host}</Text> : null}
          <FormFieldLabel color={c.textMuted}>Story URL</FormFieldLabel>
          <TextInput
            testID={testIds.suggestNewsUrl}
            value={draft.url}
            onChangeText={(url) => patch({ url })}
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="url"
            accessibilityLabel="Story URL"
            placeholder="https://example.com/story"
            placeholderTextColor={c.textMuted}
            editable={canPatch}
            style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
          />

          <FormFieldLabel color={c.textMuted}>Headline (optional)</FormFieldLabel>
          <TextInput
            testID={testIds.suggestNewsTitle}
            value={draft.title}
            onChangeText={(title) => patch({ title })}
            maxLength={titleMax}
            accessibilityLabel="Headline"
            placeholder="Suggested headline"
            placeholderTextColor={c.textMuted}
            editable={canPatch}
            style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
          />

          <FormFieldLabel color={c.textMuted}>Notes (optional)</FormFieldLabel>
          <TextInput
            testID={testIds.suggestNewsNotes}
            value={draft.notes}
            onChangeText={(notes) => patch({ notes })}
            maxLength={notesMax}
            multiline
            textAlignVertical="top"
            accessibilityLabel="Notes"
            placeholder="Why this matters"
            placeholderTextColor={c.textMuted}
            editable={canPatch}
            style={[
              styles.input,
              styles.textarea,
              { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard },
            ]}
          />

          {error ? (
            <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
              {error.message}
            </Text>
          ) : null}

          {!session.isSignedIn ? (
            <Button
              label="Sign in"
              testID={testIds.suggestNewsSignIn}
              onPress={() => {
                void (async () => {
                  await getNewsShareController().flush();
                  openSignIn(navigation, { tab: 'HomeTab', screen: 'SuggestNews' });
                })();
              }}
            />
          ) : null}

          {error?.retryable ? (
            <Button
              label="Retry"
              testID={testIds.suggestNewsRetry}
              disabled={!canSubmit}
              loading={busy}
              onPress={() => {
                if (!session.accessToken || share.kind === 'submitting') {
                  return;
                }
                if (share.kind === 'form' || share.kind === 'failed') {
                  void share.submit(session.accessToken);
                }
              }}
            />
          ) : null}

          <Button
            label="Submit"
            testID={testIds.suggestNewsSubmit}
            disabled={!canSubmit}
            loading={busy}
            onPress={() => {
              if (!session.accessToken || share.kind === 'submitting') {
                return;
              }
              if (share.kind === 'form' || share.kind === 'failed') {
                void share.submit(session.accessToken);
              }
            }}
          />
          <Button
            label="Cancel"
            variant="outline"
            testID={testIds.suggestNewsCancel}
            onPress={() => {
              if (share.kind === 'form' || share.kind === 'failed' || share.kind === 'submitting') {
                if (share.kind !== 'submitting') {
                  share.cancel();
                }
              }
              goBackOrHome();
            }}
          />
        </View>
    </FormScreenLayout>
  );
}

function ScreenShell({
  children,
  insetsBottom,
  background,
  testID,
}: {
  children: ReactNode;
  insetsBottom: number;
  background: string;
  testID?: string;
}) {
  return (
    <ScrollView
      testID={testID ?? testIds.suggestNewsScreen}
      style={[styles.flex, { backgroundColor: background }]}
      contentContainerStyle={[styles.content, { paddingBottom: insetsBottom + space.xxl }]}
    >
      {children}
    </ScrollView>
  );
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
  fields: {
    gap: space.sm,
  },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
    fontFamily: fonts.body,
    fontSize: type.body.fontSize,
  },
  textarea: {
    minHeight: 120,
  },
  candidate: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.base,
    gap: space.xs,
  },
});
