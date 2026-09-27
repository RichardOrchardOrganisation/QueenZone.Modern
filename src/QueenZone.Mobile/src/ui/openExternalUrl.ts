import { Alert, Linking } from 'react-native';
import { isHttpUrl } from './html/resolveContentUrl';

export const openExternalUrlCopy = {
  title: 'Could not open link',
  body: 'This link could not be opened on this device.',
} as const;

/**
 * Open an http(s) URL in the system browser.
 * Never rejects — `Linking.openURL` failures are caught so they cannot become
 * unhandled promise rejections (Sentry QUEENZONE-MOBILE-C / #1608).
 */
export async function openExternalUrl(url: string): Promise<boolean> {
  if (!isHttpUrl(url)) {
    Alert.alert(openExternalUrlCopy.title, openExternalUrlCopy.body);
    return false;
  }

  try {
    await Linking.openURL(url);
    return true;
  } catch {
    Alert.alert(openExternalUrlCopy.title, openExternalUrlCopy.body);
    return false;
  }
}
