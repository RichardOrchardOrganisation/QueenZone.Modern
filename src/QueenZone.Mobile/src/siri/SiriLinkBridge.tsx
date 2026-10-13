import { useNavigation } from '@react-navigation/native';
import * as Linking from 'expo-linking';
import { useEffect, useRef } from 'react';
import { Platform } from 'react-native';
import { isWidgetDeepLinkUrl } from '../widgets/widgetDeepLink';
import { parseSiriDeepLink } from './siriDeepLink';

type SiriNavigation = {
  navigate: (
    name: 'Tabs',
    params:
      | { screen: 'NewsTab'; params: { screen: 'NewsIndex'; initial: false } }
      | { screen: 'ArchiveTab'; params: { screen: 'Album'; params: { id: number }; initial: false } }
      | { screen: 'ArchiveTab'; params: { screen: 'BiographyChapter'; params: { id: number }; initial: false } }
      | { screen: 'ArchiveTab'; params: { screen: 'Search'; params: { query: string }; initial: false } },
  ) => void;
};

let consumedInitialSiriUrl = false;

export function SiriLinkBridge() {
  const navigation = useNavigation<SiriNavigation>();
  const navigationRef = useRef(navigation);
  navigationRef.current = navigation;

  useEffect(() => {
    if (Platform.OS !== 'ios') return;
    function handleUrl(url: string) {
      // Shared widget/Siri URLs have one owner: WidgetLinkBridge.
      if (isWidgetDeepLinkUrl(url)) return;
      const destination = parseSiriDeepLink(url);
      if (destination?.kind === 'news') {
        navigationRef.current.navigate('Tabs', {
          screen: 'NewsTab',
          params: { screen: 'NewsIndex', initial: false },
        });
      } else if (destination?.kind === 'search') {
        navigationRef.current.navigate('Tabs', {
          screen: 'ArchiveTab',
          params: { screen: 'Search', params: { query: destination.query }, initial: false },
        });
      } else if (destination?.kind === 'album') {
        navigationRef.current.navigate('Tabs', {
          screen: 'ArchiveTab',
          params: { screen: 'Album', params: { id: destination.id }, initial: false },
        });
      } else if (destination?.kind === 'biography') {
        navigationRef.current.navigate('Tabs', {
          screen: 'ArchiveTab',
          params: { screen: 'BiographyChapter', params: { id: destination.id }, initial: false },
        });
      }
    }

    void Linking.getInitialURL().then((url) => {
      if (url && !consumedInitialSiriUrl && parseSiriDeepLink(url)) {
        consumedInitialSiriUrl = true;
        handleUrl(url);
      }
    }).catch(() => {});
    const subscription = Linking.addEventListener('url', ({ url }) => handleUrl(url));
    return () => subscription.remove();
  }, []);

  return null;
}
