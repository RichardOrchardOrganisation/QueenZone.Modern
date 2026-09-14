import { Platform, Pressable, Text, View } from 'react-native';
import { fonts, space, type, useTheme } from '../theme';
import { MetaLine } from './MetaLine';
import { usePressProps } from './press';

export type ThreadRowItem = {
  id: string;
  title: string;
  authorInitial: string;
  author: string;
  board: string;
  replies: string;
};

type Props = {
  item: ThreadRowItem;
  onPress: () => void;
};

export function ThreadRow({ item, onPress }: Props) {
  const { c } = useTheme();
  const press = usePressProps();

  return (
    <Pressable
      accessible
      accessibilityRole="button"
      accessibilityLabel={`${item.title}. ${item.author}. ${item.board}. ${item.replies} replies`}
      onPress={onPress}
      {...press}
      style={({ pressed }) => [
        {
          flexDirection: 'row',
          alignItems: 'flex-start',
          gap: 12,
          paddingVertical: 14,
          paddingHorizontal: space.xl,
          borderTopWidth: 1,
          borderTopColor: c.hairline,
        },
        Platform.OS === 'ios' && pressed ? { backgroundColor: c.accentTintWeak } : null,
      ]}
    >
      <View
        style={{
          width: 36,
          height: 36,
          borderRadius: 18,
          backgroundColor: c.surfaceCard,
          borderWidth: 1,
          borderColor: c.border,
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Text style={{ fontFamily: fonts.display, fontSize: 14, color: c.textPrimary }}>{item.authorInitial}</Text>
      </View>
      <View style={{ flex: 1, gap: 7 }}>
        <Text numberOfLines={2} style={[type.listTitle, { color: c.textPrimary }]}>
          {item.title}
        </Text>
        <MetaLine parts={[item.author, item.board]} />
      </View>
      <Text style={[type.listTitle, { color: c.textMuted, minWidth: 28, textAlign: 'right' }]}>{item.replies}</Text>
    </Pressable>
  );
}
