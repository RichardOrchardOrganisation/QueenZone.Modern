import { StyleSheet, Text, View } from 'react-native';
import { RichHtmlBody } from './RichHtmlBody';
import { space, type, useTheme } from '../theme';

export function StoryContent({
  category,
  accentColor,
  title,
  published,
  excerpt,
  body,
}: {
  category: string;
  accentColor: string;
  title: string;
  published: string | null;
  excerpt: string | null;
  body: string;
}) {
  const { c } = useTheme();

  return (
    <>
      <Text style={[type.eyebrow, { color: accentColor }]}>{category}</Text>
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.sm }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {title}
      </Text>
      {published ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>{published}</Text>
      ) : null}
      {excerpt ? (
        <Text style={[type.standfirst, { color: c.textSecondary, marginTop: space.lg }]}>{excerpt}</Text>
      ) : null}
      <View style={styles.body}>
        <RichHtmlBody html={body} horizontalInset={26} />
      </View>
    </>
  );
}

const styles = StyleSheet.create({ body: { marginTop: space.xl } });
