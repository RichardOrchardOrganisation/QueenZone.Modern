import { Alert, Linking } from 'react-native';
import {
  getPermissionsAsync,
  requestPermissionsAsync,
  saveToLibraryAsync,
} from 'expo-media-library/legacy';
import { composerAttachCopy } from '../screens/forum/composerMeta';
import {
  SaveToPhotosError,
  saveLocalFileToPhotos,
  savePhotosPermission,
  saveToPhotosCopy,
} from './saveToPhotos';

const getPermissions = getPermissionsAsync as jest.MockedFunction<typeof getPermissionsAsync>;
const requestPermissions = requestPermissionsAsync as jest.MockedFunction<
  typeof requestPermissionsAsync
>;
const saveToLibrary = saveToLibraryAsync as jest.MockedFunction<typeof saveToLibraryAsync>;

beforeEach(() => {
  getPermissions.mockResolvedValue({
    granted: true,
    canAskAgain: true,
    status: 'granted',
  } as never);
  requestPermissions.mockResolvedValue({
    granted: true,
    canAskAgain: true,
    status: 'granted',
  } as never);
  saveToLibrary.mockResolvedValue(undefined);
  jest.spyOn(Alert, 'alert');
  jest.spyOn(Linking, 'openSettings').mockResolvedValue(undefined);
});

describe('saveLocalFileToPhotos', () => {
  it('saves with add-only permission and does not reuse the picker string', async () => {
    await saveLocalFileToPhotos('file:///cache/scan.jpg');

    expect(getPermissions).toHaveBeenCalledWith(true);
    expect(requestPermissions).not.toHaveBeenCalled();
    expect(saveToLibrary).toHaveBeenCalledWith('file:///cache/scan.jpg');
    expect(savePhotosPermission).not.toBe(
      'QueenZone accesses photos you choose so you can, for example, submit a concert photo to our moderated gallery, attach it to a forum post, or set your member avatar.',
    );
    expect(saveToPhotosCopy.denied).not.toBe(composerAttachCopy.photosPermission);
  });

  it('requests add-only permission on the first save tap', async () => {
    getPermissions.mockResolvedValueOnce({
      granted: false,
      canAskAgain: true,
      status: 'undetermined',
    } as never);

    await saveLocalFileToPhotos('file:///cache/scan.jpg');

    expect(requestPermissions).toHaveBeenCalledWith(true);
    expect(saveToLibrary).toHaveBeenCalledWith('file:///cache/scan.jpg');
    expect(Alert.alert).not.toHaveBeenCalled();
  });

  it('prompts Settings when add-only permission is denied', async () => {
    getPermissions.mockResolvedValueOnce({
      granted: false,
      canAskAgain: false,
      status: 'denied',
    } as never);

    await expect(saveLocalFileToPhotos('file:///cache/scan.jpg')).rejects.toMatchObject({
      name: 'SaveToPhotosError',
      reason: 'permission-denied',
      message: saveToPhotosCopy.denied,
    } satisfies Partial<SaveToPhotosError>);

    expect(saveToLibrary).not.toHaveBeenCalled();
    expect(Alert.alert).toHaveBeenCalledWith(
      saveToPhotosCopy.settingsTitle,
      saveToPhotosCopy.settingsBody,
      expect.arrayContaining([
        expect.objectContaining({ text: saveToPhotosCopy.cancel }),
        expect.objectContaining({ text: saveToPhotosCopy.settingsAction }),
      ]),
    );

    const buttons = (Alert.alert as jest.Mock).mock.calls[0]?.[2] as {
      text: string;
      onPress?: () => void;
    }[];
    buttons.find((button) => button.text === saveToPhotosCopy.settingsAction)?.onPress?.();
    expect(Linking.openSettings).toHaveBeenCalled();
  });

  it('does not crash when the library save fails', async () => {
    saveToLibrary.mockRejectedValueOnce(new Error('disk full'));

    await expect(saveLocalFileToPhotos('file:///cache/scan.jpg')).rejects.toMatchObject({
      name: 'SaveToPhotosError',
      reason: 'save-failed',
      message: saveToPhotosCopy.failed,
    });
  });
});
