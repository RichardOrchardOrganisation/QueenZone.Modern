import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { ApiError, forumPostReportCategories, reportForumPost, type ForumPostReportCategory } from '../../api';
import type { ForumStackParamList } from '../../navigation/types';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { Button } from '../../ui/Button';
import { radius, space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ForumStackParamList, 'ForumReport'>;

export function ForumReportScreen(props: Props) {
  return <MemberGate title="Report post"><ForumReportForm {...props} /></MemberGate>;
}

function ForumReportForm({ navigation, route }: Props) {
  const { c } = useTheme();
  const { accessToken } = useSession();
  const [category, setCategory] = useState<ForumPostReportCategory | null>(null);
  const [details, setDetails] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);

  const submit = async () => {
    if (!accessToken || !category || details.length > 1000) return;
    setSubmitting(true); setError(null);
    try {
      await reportForumPost(accessToken, route.params.postId, category, details);
      setSubmitted(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not submit this report.');
    } finally { setSubmitting(false); }
  };

  if (submitted) {
    return <View style={[styles.success, { backgroundColor: c.surfacePage }]}>
      <Text style={[type.articleTitle, { color: c.textPrimary }]}>Report submitted</Text>
      <Text style={[type.body, { color: c.textSecondary, marginTop: space.md }]}>Moderators can now review the post and its context.</Text>
      <View style={{ marginTop: space.xl }}><Button label="Return to thread" onPress={() => navigation.navigate('Thread', { id: route.params.threadId, title: route.params.threadTitle, postId: route.params.postId, reportedPostId: route.params.postId })} /></View>
    </View>;
  }

  return <ScrollView style={{ backgroundColor: c.surfacePage }} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
    <Text style={[type.body, { color: c.textSecondary }]}>Report {route.params.authorUsername}'s post. Your identity is not shown to the author.</Text>
    <Text style={[type.listTitle, { color: c.textPrimary, marginTop: space.xl }]}>Reason</Text>
    {forumPostReportCategories.map((item) => <Pressable key={item} accessibilityRole="radio" accessibilityState={{ checked: category === item }} onPress={() => setCategory(item)} style={[styles.option, { borderColor: category === item ? c.accentPrimary : c.hairline }]}>
      <Text style={[type.body, { color: c.textPrimary }]}>{category === item ? '● ' : '○ '}{item}</Text>
    </Pressable>)}
    <Text style={[type.listTitle, { color: c.textPrimary, marginTop: space.xl }]}>Supporting details (optional)</Text>
    <TextInput multiline maxLength={1000} value={details} onChangeText={setDetails} accessibilityLabel="Supporting details" style={[styles.input, { color: c.textPrimary, borderColor: c.hairline, backgroundColor: c.surfaceRaised }]} />
    <Text style={[type.caption, { color: c.textMuted, textAlign: 'right' }]}>{details.length}/1000</Text>
    {error ? <Text accessibilityRole="alert" style={[type.body, { color: c.danger, marginTop: space.md }]}>{error}</Text> : null}
    <View style={{ marginTop: space.xl }}><Button label={submitting ? 'Submitting…' : 'Submit report'} disabled={!category || submitting} onPress={() => void submit()} /></View>
    <Text style={[type.caption, { color: c.textMuted, marginTop: space.lg }]}>Community rules and support contact details are available in Settings.</Text>
  </ScrollView>;
}

const styles = StyleSheet.create({
  content: { padding: space.xl, paddingBottom: space.section },
  success: { flex: 1, padding: space.xl, justifyContent: 'center' },
  option: { borderWidth: 1, borderRadius: radius.md, padding: space.md, marginTop: space.sm },
  input: { minHeight: 130, borderWidth: 1, borderRadius: radius.md, padding: space.md, marginTop: space.sm, textAlignVertical: 'top' },
});
