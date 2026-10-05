import { Text } from 'react-native';
import { dark, type, useTheme } from '../theme';

type Props = {
  parts: string[];
  muted?: boolean;
  tone?: 'default' | 'onDark';
};

export function MetaLine({ parts, muted = true, tone = 'default' }: Props) {
  const { c } = useTheme();
  const themedColor = muted ? c.textMuted : c.textSecondary;
  const color = tone === 'onDark' ? dark.textMuted : themedColor;
  return (
    <Text maxFontSizeMultiplier={1.6} style={[type.meta, { color }]}>
      {parts.join(' · ').toUpperCase()}
    </Text>
  );
}
