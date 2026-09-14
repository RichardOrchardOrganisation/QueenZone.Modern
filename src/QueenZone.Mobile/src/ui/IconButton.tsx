import { ActivityIndicator, Platform, Pressable } from 'react-native';
import type { LucideIcon } from 'lucide-react-native';
import { useTheme } from '../theme';
import { usePressProps } from './press';

type Props = {
  icon: LucideIcon;
  onPress: () => void;
  accessibilityLabel: string;
  testID?: string;
  tone?: 'onDark' | 'accent';
  size?: 20 | 24;
  active?: boolean;
  disabled?: boolean;
  busy?: boolean;
};

export function IconButton({
  icon: Icon,
  onPress,
  accessibilityLabel,
  testID,
  tone = 'onDark',
  size = 20,
  active = false,
  disabled = false,
  busy = false,
}: Props) {
  const { c } = useTheme();
  const press = usePressProps(true);
  const color = tone === 'accent' || active ? c.accentPrimary : c.textPrimary;
  const inactive = disabled || busy;

  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled: inactive, busy }}
      disabled={inactive}
      onPress={onPress}
      {...press}
      style={({ pressed }) => [
        { width: 44, height: 44, alignItems: 'center', justifyContent: 'center', borderRadius: 22 },
        inactive ? { opacity: 0.45 } : null,
        Platform.OS === 'ios' && pressed && !inactive ? { opacity: 0.6 } : null,
      ]}
    >
      {busy ? (
        <ActivityIndicator size="small" color={color} />
      ) : (
        <Icon size={size} color={color} strokeWidth={1.5} />
      )}
    </Pressable>
  );
}
