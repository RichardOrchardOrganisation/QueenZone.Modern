import { Alert, Linking } from 'react-native';
import { openExternalUrl, openExternalUrlCopy } from './openExternalUrl';

const youtubeUrl = 'https://www.youtube.com/watch?v=1GfZoSuG8WY';

describe('openExternalUrl', () => {
  beforeEach(() => {
    jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    jest.spyOn(Alert, 'alert');
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('opens a safe https URL', async () => {
    await expect(openExternalUrl(youtubeUrl)).resolves.toBe(true);

    expect(Linking.openURL).toHaveBeenCalledWith(youtubeUrl);
    expect(Alert.alert).not.toHaveBeenCalled();
  });

  it('does not throw or leave an unhandled rejection when openURL fails', async () => {
    const openError = new Error(`Unable to open URL: ${youtubeUrl}`);
    jest.spyOn(Linking, 'openURL').mockRejectedValue(openError);

    const unhandled: unknown[] = [];
    const onUnhandled = (reason: unknown) => {
      unhandled.push(reason);
    };
    process.on('unhandledRejection', onUnhandled);

    try {
      await expect(openExternalUrl(youtubeUrl)).resolves.toBe(false);
      void openExternalUrl(youtubeUrl);
      await new Promise<void>((resolve) => {
        setImmediate(resolve);
      });

      expect(Linking.openURL).toHaveBeenCalledWith(youtubeUrl);
      expect(Alert.alert).toHaveBeenCalledWith(openExternalUrlCopy.title, openExternalUrlCopy.body);
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', onUnhandled);
    }
  });

  it('alerts and skips Linking for a non-http URL', async () => {
    await expect(openExternalUrl('javascript:alert(1)')).resolves.toBe(false);

    expect(Linking.openURL).not.toHaveBeenCalled();
    expect(Alert.alert).toHaveBeenCalledWith(openExternalUrlCopy.title, openExternalUrlCopy.body);
  });
});
