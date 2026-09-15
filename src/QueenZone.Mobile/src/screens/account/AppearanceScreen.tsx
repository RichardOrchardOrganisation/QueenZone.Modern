import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { Pressable, ScrollView, Text, View } from 'react-native';
import type { HomeStackParamList } from '../../navigation/types';
import { radius, space, type, useTheme, type ThemePreference } from '../../theme';
import { Eyebrow } from '../../ui/Eyebrow';

type Props = NativeStackScreenProps<HomeStackParamList, 'Appearance'>;

const options: readonly { value: ThemePreference; title: string; description: string }[] = [
  {
    value: 'system',
    title: 'Use system setting',
    description: 'Automatically match your phone’s Light or Dark appearance.',
  },
  { value: 'dark', title: 'Dark', description: 'Gold accents on a dark background.' },
  { value: 'light', title: 'Light', description: 'Blue accents on a light background.' },
];

export function AppearanceScreen(_: Props) {
  const { c, preference, setPreference } = useTheme();

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      contentContainerStyle={{ paddingBottom: space.section }}
    >
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.md, gap: space.sm }}>
        <Eyebrow tone="muted">Colour scheme</Eyebrow>
        <Text style={[type.body, { color: c.textSecondary }]}>Choose how QueenZone looks on this device.</Text>
      </View>
      <View style={{ paddingHorizontal: space.xl, gap: space.md }}>
        {options.map((option) => {
          const selected = preference === option.value;
          return (
            <Pressable
              key={option.value}
              accessibilityRole="radio"
              accessibilityState={{ selected }}
              accessibilityLabel={option.title}
              onPress={() => setPreference(option.value)}
              style={{
                minHeight: 72,
                borderWidth: 1,
                borderColor: selected ? c.accentPrimary : c.border,
                borderRadius: radius.sm,
                backgroundColor: c.surfaceCard,
                paddingHorizontal: space.base,
                paddingVertical: space.md,
                justifyContent: 'center',
                gap: space.xs,
              }}
            >
              <Text style={[type.listTitle, { color: c.textPrimary }]}>{option.title}</Text>
              <Text style={[type.caption, { color: c.textMuted }]}>{option.description}</Text>
            </Pressable>
          );
        })}
      </View>
    </ScrollView>
  );
}
