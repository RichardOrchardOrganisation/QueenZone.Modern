import { ScrollView, Text } from 'react-native';
import { space, type, useTheme } from '../../theme';
import { ArchiveFooter } from '../../ui/ArchiveFooter';
import { PageTitleBlock } from '../../ui/PageTitleBlock';

export function AboutArchiveScreen() {
  const { c } = useTheme();
  return (
    <ScrollView style={{ flex: 1, backgroundColor: c.surfacePage }}>
      <PageTitleBlock eyebrow="The old site" title="Queenzone.com, preserved" />
      <Text
        style={[
          type.body,
          {
            color: c.textSecondary,
            paddingHorizontal: 26,
            paddingBottom: space.xl,
          },
        ]}
      >
        This is a companion app for Queenzone.org.{"\n\n"}
        Queenzone.org is an archive of Queenzone.com, restored so that its public history can be found,
        read and enjoyed again.{"\n\n"}
        Queenzone began at the end of 1995 as Richard&apos;s Queen Page, a small fan site that grew into a
        long-running home for Queen news, articles, photography and community discussion. The original
        site was retired in 2020.{"\n\n"}
        With the help of modern tools, and with AI making this kind of careful restoration far more
        enjoyable, I decided to bring the public archive back to life. I no longer have the original
        Queenzone.com domain, so the archive now lives here at Queenzone.org.{"\n\n"}
        The goal is simple: preserve the useful public material from the old site, make it easier to
        explore, and keep it available for Queen fans who still remember the place - and for those
        discovering it for the first time.{"\n\n"}
        Regards,{"\n"}
        Richard Orchard{"\n"}
        www.richardorchard.com
      </Text>
      <ArchiveFooter />
    </ScrollView>
  );
}
