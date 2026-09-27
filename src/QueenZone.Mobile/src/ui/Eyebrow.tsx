import { Text } from 'react-native';
import { dark, type, useTheme } from '../theme';

type Tone = 'accent' | 'primary' | 'muted' | 'onDark';

type Props = {
  children: string;
  tone?: Tone;
  size?: number;
};

export function Eyebrow({ children, tone = 'accent', size = 10 }: Props) {
  const { c } = useTheme();
  const color =
    tone === 'accent'
      ? c.accentPrimary
      : tone === 'onDark'
        ? dark.textPrimary
        : tone === 'primary'
          ? c.textPrimary
          : c.textSecondary;

  return (
    <Text
      maxFontSizeMultiplier={1.4}
      style={[type.eyebrow, { fontSize: size, letterSpacing: size * 0.22, color }]}
    >
      {children}
    </Text>
  );
}
