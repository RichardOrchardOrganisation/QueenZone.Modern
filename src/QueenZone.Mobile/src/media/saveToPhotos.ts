import { Alert, Linking } from 'react-native';
import {
  getPermissionsAsync,
  requestPermissionsAsync,
  saveToLibraryAsync,
} from 'expo-media-library/legacy';

/** Info.plist `NSPhotoLibraryAddUsageDescription` — not image-picker `photosPermission`. */
export const savePhotosPermission = 'QueenZone adds a picture to your photo library when you tap Save, for example to keep a Queen gallery photo on your device.';

export const saveToPhotosCopy = {
  denied: 'Photo library permission is required to save this picture.',
  failed: 'Unable to save this picture.',
  settingsTitle: 'Photos permission needed',
  settingsBody: 'QueenZone needs permission to save this picture. You can enable it in Settings.',
  settingsAction: 'Open Settings',
  cancel: 'Cancel',
} as const;

export class SaveToPhotosError extends Error {
  readonly reason: 'permission-denied' | 'save-failed';

  constructor(reason: 'permission-denied' | 'save-failed', message: string) {
    super(message);
    this.name = 'SaveToPhotosError';
    this.reason = reason;
  }
}

/**
 * Add-only Photos save for a local `file://` URI.
 * Requests permission on the first call, not at launch. Denied opens Settings.
 */
export async function saveLocalFileToPhotos(fileUri: string): Promise<void> {
  const granted = await ensureAddOnlyPhotosPermission();
  if (!granted) {
    promptPhotosSettings();
    throw new SaveToPhotosError('permission-denied', saveToPhotosCopy.denied);
  }

  try {
    await saveToLibraryAsync(fileUri);
  } catch (error: unknown) {
    if (error instanceof SaveToPhotosError) {
      throw error;
    }
    throw new SaveToPhotosError('save-failed', saveToPhotosCopy.failed);
  }
}

async function ensureAddOnlyPhotosPermission(): Promise<boolean> {
  const existing = await getPermissionsAsync(true);
  if (existing.granted) {
    return true;
  }
  if (!existing.canAskAgain) {
    return false;
  }
  const requested = await requestPermissionsAsync(true);
  return requested.granted;
}

function promptPhotosSettings(): void {
  Alert.alert(saveToPhotosCopy.settingsTitle, saveToPhotosCopy.settingsBody, [
    { text: saveToPhotosCopy.cancel, style: 'cancel' },
    {
      text: saveToPhotosCopy.settingsAction,
      onPress: () => {
        void Linking.openSettings();
      },
    },
  ]);
}
