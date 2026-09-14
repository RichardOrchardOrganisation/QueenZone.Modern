import { Image } from 'expo-image';
import { Pressable, Text, View } from 'react-native';
import { media } from '../content/media';
import { fonts, radius, space, type, useTheme } from '../theme';
import { Button } from './Button';
import { Eyebrow } from './Eyebrow';

type Props = {
  eyebrow: string;
  numeral?: string;
  body?: string;
  quote?: { text: string; whoSaid: string };
  actionLabel?: string;
  onAction?: () => void;
  onPress?: () => void;
  testID?: string;
};

export function FeatureBlock({
  eyebrow,
  numeral,
  body,
  quote,
  actionLabel,
  onAction,
  onPress,
  testID,
}: Props) {
  const { c } = useTheme();
  const card = (
    <View
      style={{
        marginTop: space.xxl,
        marginHorizontal: space.xl,
        padding: 22,
        backgroundColor: c.surfaceRaised,
        borderWidth: 1,
        borderColor: c.borderStrong,
        borderRadius: radius.sm,
        overflow: 'hidden',
        gap: space.md,
      }}
    >
      <Image
        source={c.crest === 'crest-white.png' ? media.crestWhite : media.crestBlack}
        style={{
          position: 'absolute',
          right: -30,
          bottom: -34,
          height: 150,
          width: 150,
          opacity: c.crestWatermarkOpacity,
        }}
        contentFit="contain"
        importantForAccessibility="no"
        accessibilityElementsHidden
      />
      <Eyebrow>{eyebrow}</Eyebrow>
      {numeral ? <Text style={[type.numeral, { color: c.textPrimary }]}>{numeral}</Text> : null}
      {body ? (
        <Text style={{ fontFamily: fonts.body, fontSize: 15, lineHeight: 24, color: c.textSecondary }}>{body}</Text>
      ) : null}
      {quote ? (
        <View
          style={{
            borderTopWidth: 1,
            borderTopColor: c.borderStrong,
            paddingTop: space.md,
            gap: 6,
          }}
        >
          <Text
            style={{
              fontFamily: fonts.body,
              fontStyle: 'italic',
              fontSize: 14,
              lineHeight: 21,
              color: c.textSecondary,
            }}
          >
            “{quote.text}”
          </Text>
          <Text style={{ fontFamily: fonts.bodyMedium, fontSize: 12, color: c.textMuted }}>
            — {quote.whoSaid}
          </Text>
        </View>
      ) : null}
      {actionLabel && onAction ? (
        <View style={{ alignSelf: 'flex-start', marginTop: space.xs }}>
          <Button variant="outline" size="sm" label={actionLabel} onPress={onAction} />
        </View>
      ) : null}
    </View>
  );

  if (!onPress) {
    return card;
  }

  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={eyebrow}
      onPress={onPress}
    >
      {card}
    </Pressable>
  );
}
