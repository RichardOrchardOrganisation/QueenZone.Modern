import { useNavigation } from '@react-navigation/native';
import { addEventListener, useLinkingURL } from 'expo-linking';
import { useEffect, useRef } from 'react';
import { openCrosswordLink, type CrosswordNavigation } from './deepLink';
export function CrosswordLinkBridge() {
  const navigation = useNavigation<CrosswordNavigation>();
  const url = useLinkingURL();
  const current = useRef(navigation);
  current.current = navigation;
  const lastDelivery = useRef({ url: '', at: 0 });
  const handle = useRef((value: string) => {
    const at = Date.now();
    if (lastDelivery.current.url === value && at - lastDelivery.current.at < 500) return;
    if (openCrosswordLink(current.current, value)) lastDelivery.current = { url: value, at };
  });
  useEffect(() => {
    if (url) handle.current(url);
  }, [url]);
  // Native events also handle reopening the same URL after leaving the puzzle;
  // the scene-aware hook's string value would otherwise remain unchanged.
  useEffect(() => {
    const subscription = addEventListener('url', event => handle.current(event.url));
    return () => subscription.remove();
  }, []);
  return null;
}
