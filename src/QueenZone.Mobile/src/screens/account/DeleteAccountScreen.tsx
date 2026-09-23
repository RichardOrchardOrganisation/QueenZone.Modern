import { useCallback, useEffect, useState } from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ScrollView, Text, TextInput } from 'react-native';
import { fetchJson, sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import { getAppConfig } from '../../config/appConfig';
import {
  parseDeletionRequested,
  parseMemberProfile,
  type AccountDeletionInfo,
  type MemberProfile,
} from '../../api/me';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { openExternalUrl } from '../../ui/openExternalUrl';

const deletionReceiptStorageKey = 'queenzone.accountDeletionReceipt';

type DeletionReceipt = { token: string; title: string; message: string };

export function DeleteAccountScreen() {
  const [receipt, setReceipt] = useState<DeletionReceipt | null>(null);
  const { isSignedIn } = useSession();

  useEffect(() => {
    void AsyncStorage.getItem(deletionReceiptStorageKey).then((stored) => {
      if (stored) {
        try {
          const parsed = JSON.parse(stored) as DeletionReceipt;
          if (typeof parsed.token === 'string') {
            setReceipt(parsed);
          }
        } catch {
          // A damaged local receipt does not block account settings.
        }
      }
    }).catch(() => undefined);
  }, []);

  if (receipt) {
    return <DeletionReceiptView receipt={receipt} canStartNew={isSignedIn} onClear={() => {
      void AsyncStorage.removeItem(deletionReceiptStorageKey);
      setReceipt(null);
    }} />;
  }

  return (
    <MemberGate title="Delete account">
      <DeleteAccountForm onReceipt={setReceipt} />
    </MemberGate>
  );
}

function DeletionReceiptView({
  receipt,
  canStartNew,
  onClear,
}: {
  receipt: DeletionReceipt;
  canStartNew: boolean;
  onClear: () => void;
}) {
  const { c } = useTheme();
  const [status, setStatus] = useState<'processing' | 'complete'>('processing');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await fetchJson('/account-deletion-status', { query: { receipt: receipt.token } }) as { status?: string };
      setStatus(result.status === 'complete' ? 'complete' : 'processing');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not check deletion status.');
    } finally {
      setBusy(false);
    }
  }, [receipt.token]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const statusUrl = `${getAppConfig().apiBaseUrl}/account/deletion-status?receipt=${encodeURIComponent(receipt.token)}`;
  return (
    <ScrollView style={{ flex: 1, backgroundColor: c.surfacePage }} contentContainerStyle={{ padding: space.xl, gap: space.md }}>
      <Text style={[type.pageTitle, { color: c.textPrimary }]}>
        {status === 'complete' ? 'Account deletion complete' : receipt.title}
      </Text>
      <Text style={[type.body, { color: c.textSecondary }]}>
        {status === 'complete' ? 'Your account data and modern contributions have been removed.' : receipt.message}
      </Text>
      <Text style={[type.body, { color: c.textSecondary }]}>
        This private status receipt works after sign-out. Save the page address if you want to check later.
      </Text>
      {status === 'processing' ? <Text style={[type.body, { color: c.textSecondary }]}>
        Most requests finish within minutes. Cleanup that needs another attempt is checked every six hours.
      </Text> : null}
      {error ? <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">{error}</Text> : null}
      <Button label="Refresh deletion status" loading={busy} onPress={() => void refresh()} />
      <Button label="Open status receipt" variant="outline" onPress={() => void openExternalUrl(statusUrl)} />
      {canStartNew ? <Button label="Delete another account" variant="outline" onPress={onClear} /> : null}
    </ScrollView>
  );
}

function DeleteAccountForm({ onReceipt }: { onReceipt: (receipt: DeletionReceipt) => void }) {
  const { c } = useTheme();
  const { accessToken, refreshProfile, signOut } = useSession();
  const [profile, setProfile] = useState<MemberProfile | null>(null);
  const [confirmation, setConfirmation] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [requested, setRequested] = useState<{ title: string; message: string } | null>(null);

  const load = useCallback(async () => {
    if (!accessToken) {
      return;
    }

    try {
      setProfile(parseMemberProfile(await fetchJson('/me', { accessToken })));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load account deletion.');
    }
  }, [accessToken]);

  useEffect(() => {
    void load();
  }, [load]);

  async function requestDeletion() {
    if (!accessToken || !profile) {
      return;
    }

    if (confirmation.trim() !== profile.deletion.confirmationPhrase) {
      setError(profile.deletion.confirmationHint);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const result = parseDeletionRequested(
        await sendJson('/me/deletion-request', {
          accessToken,
          body: { confirmation: confirmation.trim(), immediate: true },
        }),
      );
      if (result.statusReceipt) {
        const receipt = { token: result.statusReceipt, title: result.title, message: result.message };
        await AsyncStorage.setItem(deletionReceiptStorageKey, JSON.stringify(receipt)).catch(() => undefined);
        onReceipt(receipt);
      } else {
        setRequested({ title: result.title, message: result.message });
      }
      await signOut();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not request account deletion.');
    } finally {
      setBusy(false);
    }
  }

  async function cancelDeletion() {
    if (!accessToken) {
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const next = parseMemberProfile(
        await sendJson('/me/deletion-request/cancel', { accessToken }),
      );
      setProfile(next);
      await refreshProfile();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not cancel account deletion.');
    } finally {
      setBusy(false);
    }
  }

  const deletion: AccountDeletionInfo | undefined = profile?.deletion;

  if (requested) {
    return (
      <ScrollView
        style={{ flex: 1, backgroundColor: c.surfacePage }}
        contentContainerStyle={{ padding: space.xl, gap: space.md }}
      >
        <Text style={[type.pageTitle, { color: c.textPrimary }]}>{requested.title}</Text>
        <Text style={[type.body, { color: c.textSecondary }]}>{requested.message}</Text>
      </ScrollView>
    );
  }

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      contentContainerStyle={{ padding: space.xl, gap: space.md, paddingBottom: space.section }}
    >
      {error ? (
        <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
          {error}
        </Text>
      ) : null}
      {profile?.scheduledDeletionAt ? (
        <>
          <Text style={[type.pageTitle, { color: c.textPrimary }]}>Deletion scheduled</Text>
          <Text style={[type.body, { color: c.textSecondary }]}>
            Your account is scheduled for permanent deletion. Your public identity is anonymised during the 30-day
            cooling-off period. Sign-in remains available so you can cancel deletion below.
          </Text>
          <Button label="Cancel account deletion" loading={busy} onPress={() => void cancelDeletion()} />
        </>
      ) : (
        <>
          <Text style={[type.pageTitle, { color: c.textPrimary }]}>What happens</Text>
          {(deletion?.whatHappens ?? []).map((item) => (
            <Text key={item} style={[type.body, { color: c.textSecondary }]}>
              • {item}
            </Text>
          ))}
          <Text style={[type.body, { color: c.textSecondary }]}>Deletion cannot be undone.</Text>
          <Text style={[type.body, { color: c.textSecondary }]}>
            If you use Sign in with Apple, you can also remove QueenZone under iPhone Settings → your name → Sign in with Apple.
          </Text>
          <Text style={[type.caption, { color: c.textMuted }]}>
            {deletion?.confirmationHint ?? 'Type DELETE to delete the account.'}
            {profile?.email ? ` Account: ${profile.email}` : ''}
          </Text>
          <TextInput
            value={confirmation}
            onChangeText={setConfirmation}
            autoCapitalize="characters"
            autoCorrect={false}
            accessibilityLabel="Type DELETE to confirm"
            style={{
              minHeight: 48,
              borderWidth: 1,
              borderColor: c.border,
              borderRadius: radius.xs,
              paddingHorizontal: space.md,
              color: c.textPrimary,
              ...type.body,
            }}
          />
          <Button label="Delete my account now" loading={busy} onPress={() => void requestDeletion()} />
        </>
      )}
    </ScrollView>
  );
}
