import { StatusBar } from 'expo-status-bar';
import { useMemo, type ReactNode } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaInsetsContext, useSafeAreaInsets } from 'react-native-safe-area-context';
import { getAppConfig } from '../config/appConfig';
import { testIds } from '../test/testIds';
import { fonts } from '../theme';
import { resolveEnvBannerLabel } from './envBannerLabel';

export const ENV_BANNER_HEIGHT = 28;
export const ENV_BANNER_AMBER = '#FBBF24';
export const ENV_BANNER_INK = '#111111';

type Props = {
  children: ReactNode;
};

export function EnvBanner({ children }: Props) {
  const insets = useSafeAreaInsets();
  const label = resolveEnvBannerLabel(getAppConfig().appEnv);
  const contentInsets = useMemo(() => ({ ...insets, top: 0 }), [insets]);
  if (label == null) {
    return children;
  }

  return (
    <View style={styles.column}>
      <View pointerEvents="none" style={[styles.notch, { height: insets.top }]} />
      <View testID={testIds.envBanner} pointerEvents="none" style={styles.bar}>
        <Text accessibilityRole="text" style={styles.label}>
          {label}
        </Text>
      </View>
      <StatusBar style="dark" />
      <SafeAreaInsetsContext.Provider value={contentInsets}>
        <View style={styles.column}>{children}</View>
      </SafeAreaInsetsContext.Provider>
    </View>
  );
}

const styles = StyleSheet.create({
  column: {
    flex: 1,
  },
  notch: {
    backgroundColor: ENV_BANNER_AMBER,
  },
  bar: {
    height: ENV_BANNER_HEIGHT,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: ENV_BANNER_AMBER,
  },
  label: {
    fontFamily: fonts.bodySemi,
    fontSize: 11,
    lineHeight: 14,
    letterSpacing: 1.6,
    textTransform: 'uppercase',
    color: ENV_BANNER_INK,
  },
});
