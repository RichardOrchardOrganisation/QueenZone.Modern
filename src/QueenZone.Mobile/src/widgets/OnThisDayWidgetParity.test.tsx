import React from 'react';
import { render } from '@testing-library/react-native';
import { OnThisDayWidgetView } from './OnThisDayWidget.ios';
import { widgetActiveFace, widgetDayPrimary, widgetDaySecondary, widgetEmptyText, widgetEyebrow, widgetQuotePrimary, widgetQuoteSecondary, widgetTriviaPrimary, WIDGET_FACE_SLOT_MS } from './widgetCopy';
import type { WidgetProps } from './widgetCopy';
import { widgetFaceDeepLinkUrl } from './widgetDeepLink';

jest.mock('@expo/ui/swift-ui', () => {
  const React = require('react');
  const { Text, View } = require('react-native');
  return {
    Text: ({ children }: { children: React.ReactNode }) => React.createElement(Text, null, children),
    VStack: ({ children, modifiers }: { children: React.ReactNode; modifiers: { widgetURL?: string }[] }) => React.createElement(View, { accessibilityLabel: modifiers.find((modifier) => modifier?.widgetURL)?.widgetURL }, children),
  };
});
jest.mock('@expo/ui/swift-ui/modifiers', () => ({
  containerBackground: jest.fn(), font: jest.fn(), foregroundStyle: jest.fn(), lineLimit: jest.fn(),
  minimumScaleFactor: jest.fn(), padding: jest.fn(), truncationMode: jest.fn(),
  widgetURL: (url: string) => ({ widgetURL: url }),
}));
jest.mock('expo-widgets', () => ({ createWidget: jest.fn(() => ({})) }));

const day = { formattedDate: '01 July 2026', summary: 'Queen announce a tour.', eventId: 12 };
const quote = { quoteText: 'We are ready.', quoteWhoSaid: 'Brian May', quoteId: 34 };
const trivia = { triviaText: 'Queen recorded at Mountain Studios.' };
const cases: [string, WidgetProps][] = [
  ['day', day], ['day without id', { ...day, eventId: 0 }], ['day negative id', { ...day, eventId: -1 }],
  ['quote', quote], ['quote without id', { ...quote, quoteId: undefined }],
  ['quote zero id and unrelated event', { ...quote, quoteId: 0, eventId: 12 }],
  ['quote negative id', { ...quote, quoteId: -1 }], ['trivia', trivia],
  ['day and quote', { ...day, ...quote }], ['all faces', { ...day, ...quote, ...trivia }],
  ['empty', {}], ['empty with event id', { eventId: 12 }],
  ['partial day and quote', { formattedDate: day.formattedDate, quoteText: quote.quoteText }],
];

describe('iOS widget parity with canonical Android decisions', () => {
  afterEach(() => jest.useRealTimers());
  it.each(cases)('%s across three consecutive four-hour slots', (_label, props) => {
    jest.useFakeTimers();
    for (let slot = 0; slot < 3; slot += 1) {
      const now = slot * WIDGET_FACE_SLOT_MS;
      jest.setSystemTime(now);
      const face = widgetActiveFace(props, now);
      const screen = render(<OnThisDayWidgetView {...props} />);
      expect(screen.getByLabelText(widgetFaceDeepLinkUrl(face, props.quoteId, props.eventId))).toBeTruthy();
      expect(screen.getByText(face ? widgetEyebrow(face) : 'ON THIS DAY')).toBeTruthy();
      const lines = face === 'day' ? [widgetDayPrimary(props), widgetDaySecondary(props)]
        : face === 'quote' ? [widgetQuotePrimary(props), widgetQuoteSecondary(props)]
          : [face === 'trivia' ? widgetTriviaPrimary(props) : widgetEmptyText];
      for (const line of lines) expect(screen.getByText(line)).toBeTruthy();
      for (const hidden of ['ON THIS DAY', 'QUEEN QUOTES', 'QUEEN FACTS'].filter((eyebrow) => eyebrow !== (face ? widgetEyebrow(face) : 'ON THIS DAY'))) {
        expect(screen.queryByText(hidden)).toBeNull();
      }
      screen.unmount();
    }
  });
});
