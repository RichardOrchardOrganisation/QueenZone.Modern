import { useCallback, useEffect, useRef, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { getAppConfig } from '../../config/appConfig';
import { authProvidersUrl, fallbackAuthProviders, parseAuthProviders, type AuthProvider } from '../../api/auth';
import type { RootStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { completeSignInNavigation } from '../../session/signInNavigation';
import { testIds } from '../../test/testIds';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { AppleSignInButton } from '../../ui/AppleSignInButton';
import { Button } from '../../ui/Button';
import { CrestSeal } from '../../ui/CrestSeal';

type Props = NativeStackScreenProps<RootStackParamList, 'SignIn'>;

export function SignInScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { isSignedIn, signIn, signInWithPassword } = useSession();
  const [providers, setProviders] = useState<AuthProvider[]>(fallbackAuthProviders);
  const [busyProvider, setBusyProvider] = useState<string | null>(null);
  const [otherWaysOpen, setOtherWaysOpen] = useState(false);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [passwordBusy, setPasswordBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const finished = useRef(false);
  const passwordInput = useRef<TextInput>(null);
  const formBusy = busyProvider !== null || passwordBusy;

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const response = await fetch(authProvidersUrl(getAppConfig().apiBaseUrl), {
          headers: { Accept: 'application/json' },
        });
        const payload: unknown = await response.json().catch(() => null);
        if (!cancelled && response.ok) {
          setProviders(parseAuthProviders(payload));
        }
      } catch {
        if (!cancelled) {
          setProviders(fallbackAuthProviders);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const leaveAfterSignIn = useCallback(() => {
    if (finished.current) {
      return;
    }
    finished.current = true;
    completeSignInNavigation(navigation as never, route.params?.returnTo);
  }, [navigation, route.params?.returnTo]);

  useEffect(() => {
    if (isSignedIn) {
      leaveAfterSignIn();
    }
  }, [isSignedIn, leaveAfterSignIn]);

  const onProvider = useCallback(
    async (provider: AuthProvider) => {
      if (formBusy) {
        return;
      }

      setError(null);
      setBusyProvider(provider.id);
      try {
        await signIn(provider.id);
        leaveAfterSignIn();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not sign in.');
      } finally {
        setBusyProvider(null);
      }
    },
    [formBusy, leaveAfterSignIn, signIn],
  );

  const onPasswordSignIn = useCallback(async () => {
    if (formBusy) {
      return;
    }

    const trimmedEmail = email.trim();
    if (!trimmedEmail || password.length === 0) {
      setError('Enter your email and password.');
      return;
    }

    setError(null);
    setPasswordBusy(true);
    try {
      await signInWithPassword(trimmedEmail, password);
      leaveAfterSignIn();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not sign in.');
    } finally {
      setPasswordBusy(false);
    }
  }, [email, formBusy, leaveAfterSignIn, password, signInWithPassword]);

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      keyboardShouldPersistTaps="handled"
      contentContainerStyle={{
        paddingHorizontal: space.xl,
        paddingTop: space.section,
        paddingBottom: space.section,
        gap: space.lg,
      }}
    >
      <View style={{ alignItems: 'center', gap: space.md }}>
        <CrestSeal height={48} opacity={0.5} />
        <Text style={[type.pageTitle, { color: c.textPrimary, textAlign: 'center' }]}>Sign in</Text>
        <Text style={[type.body, { color: c.textSecondary, textAlign: 'center' }]}>
          Sign in to QueenZone with Google, Microsoft, Discord, GitHub or Apple. OAuth never sends a provider
          secret to the app. Email and password is for operator-created accounts that cannot use social sign-in.
        </Text>
      </View>
      {error ? (
        <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
          {error}
        </Text>
      ) : null}
      <View style={{ gap: 10 }}>
        {providers.map((provider) => {
          const props = {
            label: provider.label,
            loading: busyProvider === provider.id,
            disabled: formBusy && busyProvider !== provider.id,
            onPress: () => {
              void onProvider(provider);
            },
          };

          return provider.id === 'Apple' ? (
            <AppleSignInButton key={provider.id} {...props} />
          ) : (
            <Button key={provider.id} {...props} variant="outline" />
          );
        })}
      </View>
      <View style={styles.fallback}>
        <Pressable
          testID={testIds.signInOtherWays}
          accessibilityRole="button"
          accessibilityState={{ expanded: otherWaysOpen }}
          onPress={() => setOtherWaysOpen((open) => !open)}
        >
          <Text style={[type.listTitle, { color: c.accentPrimary }]}>Other ways to sign in</Text>
        </Pressable>
        {otherWaysOpen ? (
          <View style={styles.fallbackFields}>
            <Text style={[type.caption, { color: c.textMuted }]}>
              For reviewers and accounts without access to a social provider.
            </Text>
            <Text style={[type.listTitle, { color: c.textMuted }]}>Email</Text>
            <TextInput
              testID={testIds.signInEmail}
              value={email}
              onChangeText={setEmail}
              autoCapitalize="none"
              autoCorrect={false}
              autoComplete="username"
              keyboardType="email-address"
              returnKeyType="next"
              blurOnSubmit={false}
              onSubmitEditing={() => passwordInput.current?.focus()}
              textContentType="username"
              accessibilityLabel="Email"
              placeholder="you@example.com"
              placeholderTextColor={c.textMuted}
              editable={!formBusy}
              style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
            />
            <Text style={[type.listTitle, { color: c.textMuted }]}>Password</Text>
            <TextInput
              ref={passwordInput}
              testID={testIds.signInPassword}
              value={password}
              onChangeText={setPassword}
              autoCapitalize="none"
              autoCorrect={false}
              autoComplete="password"
              returnKeyType="go"
              onSubmitEditing={() => {
                void onPasswordSignIn();
              }}
              textContentType="password"
              secureTextEntry
              accessibilityLabel="Password"
              placeholder="Password"
              placeholderTextColor={c.textMuted}
              editable={!formBusy}
              style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
            />
            <Button
              label="Sign in"
              testID={testIds.signInPasswordSubmit}
              loading={passwordBusy}
              disabled={formBusy && !passwordBusy}
              onPress={() => {
                void onPasswordSignIn();
              }}
            />
          </View>
        ) : null}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  fallback: {
    gap: space.sm,
  },
  fallbackFields: {
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
});
