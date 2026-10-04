import { useNavigation } from '@react-navigation/native';
import * as Linking from 'expo-linking';
import { useEffect, useRef } from 'react';
import { openCrosswordLink, type CrosswordNavigation } from './deepLink';
export function CrosswordLinkBridge() {
  const navigation = useNavigation<CrosswordNavigation>();
  const current = useRef(navigation);
  current.current = navigation;
  useEffect(() => {
    const handle = (url: string) => { openCrosswordLink(current.current, url); };
    void Linking.getInitialURL().then(url => { if (url) handle(url); }).catch(() => {});
    const subscription = Linking.addEventListener('url', event => handle(event.url));
    return () => subscription.remove();
  }, []);
  return null;
}
