import { useNavigation } from '@react-navigation/native';
import * as Linking from 'expo-linking';
import { useEffect, useRef } from 'react';
import {
  consumeInitialWidgetUrl,
  isWidgetDeepLinkUrl,
  openWidgetDestination,
  type WidgetNavigation,
} from './widgetDeepLink';

/** Handles taps on the OS home screen widget — iOS `widgetURL` and Android `OPEN_URI`.
 * Quote face with an id opens the quote page; day face opens Timeline (focus when id is present);
 * trivia face opens Archive Trivia (`queenzone://trivia`). */
export function WidgetLinkBridge() {
  const navigation = useNavigation<WidgetNavigation>();
  const navigationRef = useRef(navigation);
  navigationRef.current = navigation;

  useEffect(() => {
    let receivedWidgetEvent = false;
    function handleUrl(url: string) {
      if (isWidgetDeepLinkUrl(url)) {
        openWidgetDestination(navigationRef.current, url);
      }
    }

    Linking.getInitialURL()
      .then((url) => {
        const widgetUrl = consumeInitialWidgetUrl(url);
        if (widgetUrl && !receivedWidgetEvent) {
          handleUrl(widgetUrl);
        }
      })
      .catch(() => {
        /* no initial URL to recover */
      });

    const subscription = Linking.addEventListener('url', ({ url }) => {
      if (isWidgetDeepLinkUrl(url)) {
        receivedWidgetEvent = true;
      }
      handleUrl(url);
    });
    return () => subscription.remove();
  }, []);

  return null;
}
