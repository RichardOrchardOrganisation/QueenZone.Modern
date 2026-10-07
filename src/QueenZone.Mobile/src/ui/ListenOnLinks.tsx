import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { StreamingLink } from '../api';
import { space, type, useTheme } from '../theme';
import { Button } from './Button';
import { openExternalUrl } from './openExternalUrl';
import { usePressProps } from './press';

/** Providers this build knows, in display order. Unknown server keys are skipped. */
const providers = [
  { key: 'spotify', name: 'Spotify', origin: 'https://open.spotify.com/' },
  { key: 'apple-music', name: 'Apple Music', origin: 'https://music.apple.com/' },
] as const;

export type KnownStreamingLink = { key: string; name: string; url: string };

/**
 * Known providers with an https URL on that provider's host, in display order. Universal links
 * open the Spotify / Apple Music app when installed, otherwise the browser.
 */
export function knownStreamingLinks(links: StreamingLink[] | null | undefined): KnownStreamingLink[] {
  if (!links?.length) {
    return [];
  }

  return providers.flatMap((provider) => {
    const link = links.find((candidate) => candidate.provider === provider.key);
    return link?.url.startsWith(provider.origin)
      ? [{ key: provider.key, name: provider.name, url: link.url }]
      : [];
  });
}

type Props = Readonly<{
  links: StreamingLink[] | null | undefined;
  /** Album or song title, used in each accessibility label. */
  subject: string;
  /** Small inline text links for track and appearance rows. */
  compact?: boolean;
  testID?: string;
}>;

export function ListenOnLinks({ links, subject, compact, testID }: Props) {
  const { c } = useTheme();
  const press = usePressProps();
  const known = knownStreamingLinks(links);
  if (known.length === 0) {
    return null;
  }

  if (compact) {
    return (
      <View style={styles.compact} testID={testID}>
        {known.map((link) => (
          <Pressable
            key={link.key}
            testID={testID ? `${testID}-${link.key}` : undefined}
            accessibilityRole="link"
            accessibilityLabel={`${link.name}: listen to ${subject}`}
            accessibilityHint="Opens outside the app"
            hitSlop={8}
            onPress={() => void openExternalUrl(link.url)}
            {...press}
          >
            <Text style={[type.meta, styles.compactLabel, { color: c.textSecondary }]}>{link.name}</Text>
          </Pressable>
        ))}
      </View>
    );
  }

  return (
    <View style={styles.buttons} testID={testID}>
      {known.map((link) => (
        <Button
          key={link.key}
          testID={testID ? `${testID}-${link.key}` : undefined}
          label={`Listen on ${link.name}`}
          accessibilityLabel={`Listen on ${link.name}: ${subject}`}
          variant="outline"
          size="sm"
          onPress={() => void openExternalUrl(link.url)}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  buttons: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.sm,
    marginTop: space.lg,
  },
  compact: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.base,
    marginTop: space.xs,
  },
  compactLabel: {
    textDecorationLine: 'underline',
  },
});
