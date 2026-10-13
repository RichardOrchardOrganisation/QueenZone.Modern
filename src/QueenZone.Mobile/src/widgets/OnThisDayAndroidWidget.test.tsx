import { readFileSync } from 'node:fs';
import path from 'node:path';
import { screen } from '@testing-library/react-native';
import { OnThisDayAndroidWidget } from './OnThisDayAndroidWidget';
import { renderWithProviders } from '../test/render';
import {
  WIDGET_FACE_SLOT_MS,
  WIDGET_QUOTE_MAX_LINES,
  WIDGET_QUOTE_MAX_PT_MEDIUM,
  WIDGET_QUOTE_MAX_PT_SMALL,
  WIDGET_QUOTE_SECONDARY_MAX_LINES,
  WIDGET_QUOTE_SECONDARY_PT_MEDIUM,
  WIDGET_QUOTE_SECONDARY_PT_SMALL,
  widgetEmptyText,
  widgetPrimaryFontSize,
} from './widgetCopy';

jest.mock('react-native-android-widget', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View, Text } = require('react-native') as typeof import('react-native');
  return {
    FlexWidget: View,
    OverlapWidget: View,
    ImageWidget: () => <View testID="widget-crest" />,
    TextWidget: ({
      text,
      maxLines,
      style,
    }: {
      text: string;
      maxLines?: number;
      style?: { fontSize?: number };
    }) => (
      <Text testID={`tw:${text}:${maxLines ?? 'none'}:${style?.fontSize ?? 'default'}`}>{text}</Text>
    ),
  };
});

const bothHalves = {
  formattedDate: '30 June 1980',
  summary: 'Queen released The Game.',
  quoteText: 'A kind of magic',
  quoteWhoSaid: 'Freddie Mercury',
};

const allThree = {
  ...bothHalves,
  triviaText: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
};

const source = readFileSync(path.join(__dirname, 'OnThisDayAndroidWidget.tsx'), 'utf8');

describe('OnThisDayAndroidWidget', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('does not keep a hard maxLines={3} on a concatenated body', () => {
    expect(source).not.toMatch(/maxLines=\{3\}/);
    expect(source).not.toMatch(/widgetDayText/);
    expect(source).not.toMatch(/widgetQuoteText/);
    expect(source).toMatch(/WIDGET_QUOTE_MAX_LINES/);
    expect(source).toMatch(/WIDGET_QUOTE_SECONDARY_MAX_LINES/);
    expect(source).toMatch(/widgetFaceDeepLinkUrl\(face, props\.quoteId, props\.eventId, props\.triviaId\)/);
    expect(source).not.toMatch(/uri: widgetDeepLinkUrl/);
  });

  it('shows only the on-this-day face in an even 4-hour slot', () => {
    jest.spyOn(Date, 'now').mockReturnValue(0);
    renderWithProviders(<OnThisDayAndroidWidget {...bothHalves} />, { navigation: false });

    expect(screen.getByText('ON THIS DAY')).toBeOnTheScreen();
    expect(screen.getByText('Queen released The Game.')).toBeOnTheScreen();
    expect(screen.getByText('30 June 1980')).toBeOnTheScreen();
    expect(screen.queryByText('QUEEN QUOTES')).toBeNull();
    expect(screen.queryByText('“A kind of magic”')).toBeNull();
    expect(screen.queryByText('30 June 1980: Queen released The Game.')).toBeNull();
    expect(screen.getByTestId('widget-crest')).toBeOnTheScreen();
    expect(
      screen.getByTestId(
        `tw:Queen released The Game.:${WIDGET_QUOTE_MAX_LINES}:${WIDGET_QUOTE_MAX_PT_SMALL}`,
      ),
    ).toBeOnTheScreen();
    expect(
      screen.getByTestId(`tw:30 June 1980:${WIDGET_QUOTE_SECONDARY_MAX_LINES}:${WIDGET_QUOTE_SECONDARY_PT_SMALL}`),
    ).toBeOnTheScreen();
  });

  it('shows only the Queen Quotes face in an odd 4-hour slot', () => {
    jest.spyOn(Date, 'now').mockReturnValue(WIDGET_FACE_SLOT_MS);
    renderWithProviders(<OnThisDayAndroidWidget {...bothHalves} />, { navigation: false });

    expect(screen.getByText('QUEEN QUOTES')).toBeOnTheScreen();
    expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen();
    expect(screen.getByText('— Freddie Mercury')).toBeOnTheScreen();
    expect(screen.queryByText('ON THIS DAY')).toBeNull();
    expect(screen.queryByText('Queen released The Game.')).toBeNull();
    expect(screen.queryByText('“A kind of magic” — Freddie Mercury')).toBeNull();
    expect(
      screen.getByTestId(`tw:“A kind of magic”:${WIDGET_QUOTE_MAX_LINES}:${WIDGET_QUOTE_MAX_PT_SMALL}`),
    ).toBeOnTheScreen();
    expect(
      screen.getByTestId(`tw:— Freddie Mercury:${WIDGET_QUOTE_SECONDARY_MAX_LINES}:${WIDGET_QUOTE_SECONDARY_PT_SMALL}`),
    ).toBeOnTheScreen();
  });

  it('uses the medium ceiling and secondary size on a 4×2 span', () => {
    jest.spyOn(Date, 'now').mockReturnValue(WIDGET_FACE_SLOT_MS);
    renderWithProviders(<OnThisDayAndroidWidget {...bothHalves} widgetWidth={300} />, { navigation: false });

    expect(
      screen.getByTestId(`tw:“A kind of magic”:${WIDGET_QUOTE_MAX_LINES}:${WIDGET_QUOTE_MAX_PT_MEDIUM}`),
    ).toBeOnTheScreen();
    expect(
      screen.getByTestId(
        `tw:— Freddie Mercury:${WIDGET_QUOTE_SECONDARY_MAX_LINES}:${WIDGET_QUOTE_SECONDARY_PT_MEDIUM}`,
      ),
    ).toBeOnTheScreen();
  });

  it('shrinks a long primary below the short-quote ceiling', () => {
    const longQuote = 'x'.repeat(120);
    renderWithProviders(
      <OnThisDayAndroidWidget quoteText={longQuote} quoteWhoSaid="Freddie Mercury" />,
      { navigation: false },
    );

    const longPrimary = `“${longQuote}”`;
    const longSize = widgetPrimaryFontSize(longPrimary, 'small');
    expect(longSize).toBeLessThan(WIDGET_QUOTE_MAX_PT_SMALL);
    expect(
      screen.getByTestId(`tw:${longPrimary}:${WIDGET_QUOTE_MAX_LINES}:${longSize}`),
    ).toBeOnTheScreen();
    expect(screen.getByText('— Freddie Mercury')).toBeOnTheScreen();
  });

  it('shows only the Queen Facts face in the third 4-hour slot', () => {
    jest.spyOn(Date, 'now').mockReturnValue(WIDGET_FACE_SLOT_MS * 2);
    renderWithProviders(<OnThisDayAndroidWidget {...allThree} />, { navigation: false });

    expect(screen.getByText('QUEEN FACTS')).toBeOnTheScreen();
    expect(screen.getByText(allThree.triviaText)).toBeOnTheScreen();
    expect(screen.queryByText('ON THIS DAY')).toBeNull();
    expect(screen.queryByText('QUEEN QUOTES')).toBeNull();
    expect(screen.queryByText('Queen released The Game.')).toBeNull();
    expect(
      screen.getByTestId(
        `tw:${allThree.triviaText}:${WIDGET_QUOTE_MAX_LINES}:${widgetPrimaryFontSize(allThree.triviaText, 'small')}`,
      ),
    ).toBeOnTheScreen();
  });

  it('skips trivia and keeps day+quote rotation when triviaText is missing', () => {
    jest.spyOn(Date, 'now').mockReturnValue(WIDGET_FACE_SLOT_MS * 2);
    renderWithProviders(<OnThisDayAndroidWidget {...bothHalves} />, { navigation: false });

    expect(screen.getByText('ON THIS DAY')).toBeOnTheScreen();
    expect(screen.queryByText('QUEEN FACTS')).toBeNull();
  });

  it('falls back to quote-only copy when there is no event', () => {
    renderWithProviders(
      <OnThisDayAndroidWidget quoteText="A kind of magic" quoteWhoSaid="Freddie Mercury" />,
      { navigation: false },
    );

    expect(screen.getByText('QUEEN QUOTES')).toBeOnTheScreen();
    expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen();
    expect(screen.getByText('— Freddie Mercury')).toBeOnTheScreen();
  });

  it('asks the member to open the app when both halves are missing', () => {
    renderWithProviders(<OnThisDayAndroidWidget />, { navigation: false });
    expect(screen.getByText('ON THIS DAY')).toBeOnTheScreen();
    expect(screen.getByText(widgetEmptyText)).toBeOnTheScreen();
    expect(screen.queryByText('— ')).toBeNull();
    expect(
      screen.getByTestId(
        `tw:${widgetEmptyText}:${WIDGET_QUOTE_MAX_LINES}:${widgetPrimaryFontSize(widgetEmptyText, 'small')}`,
      ),
    ).toBeOnTheScreen();
  });
});
