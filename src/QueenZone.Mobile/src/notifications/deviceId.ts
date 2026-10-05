import AsyncStorage from '@react-native-async-storage/async-storage';
import * as Crypto from 'expo-crypto';

/**
 * Stable per-install identifier sent to `/notifications/devices` (#757).
 * Persists across sign-in/out on the same install — the backend keys
 * registrations by deviceId, and re-registering under a different member
 * simply moves the row rather than creating a duplicate.
 */
const deviceIdKey = 'queenzone.mobile.pushDeviceId';

export async function getOrCreateDeviceId(): Promise<string> {
  const existing = await AsyncStorage.getItem(deviceIdKey);
  if (existing) {
    return existing;
  }

  const created = await generateDeviceId();
  await AsyncStorage.setItem(deviceIdKey, created);
  return created;
}

/** Reads the stored device id without creating one — used before unregistering. */
export function peekDeviceId(): Promise<string | null> {
  return AsyncStorage.getItem(deviceIdKey);
}

async function generateDeviceId(): Promise<string> {
  const bytes = await Crypto.getRandomBytesAsync(16);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  let hex = '';
  for (const byte of bytes) {
    hex += byte.toString(16).padStart(2, '0');
  }

  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
