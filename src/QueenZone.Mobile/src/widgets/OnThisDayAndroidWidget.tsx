'use no memo';
import { FlexWidget, ImageWidget, OverlapWidget, TextWidget } from 'react-native-android-widget';
import {
  WIDGET_QUOTE_MAX_LINES,
  WIDGET_QUOTE_SECONDARY_MAX_LINES,
  widgetActiveFace,
  widgetDayPrimary,
  widgetDaySecondary,
  widgetEmptyText,
  widgetEyebrow,
  widgetFamilyFromWidth,
  widgetPrimaryFontSize,
  widgetQuotePrimary,
  widgetQuoteSecondary,
  widgetSecondaryPt,
  widgetTriviaPrimary,
  type WidgetProps,
} from './widgetCopy';
import { widgetFaceDeepLinkUrl } from './widgetDeepLink';

/**
 * Props rendered into the widget's bitmap (the library rasterizes this tree, it does not
 * host live RN views). Same shape as OnThisDayWidget.ios.tsx.
 */
export type OnThisDayAndroidWidgetProps = WidgetProps & {
  widgetWidth?: number;
};

const gold = '#B89A4A';
const cream = '#F2F1ED';
const mutedCream = '#B8B6B0';
const cardBackground = '#181614';

export function OnThisDayAndroidWidget(props: OnThisDayAndroidWidgetProps) {
  const face = widgetActiveFace(props);
  const family = widgetFamilyFromWidth(props.widgetWidth);
  const secondarySize = widgetSecondaryPt(family);
  const dayPrimary = widgetDayPrimary(props);
  const quotePrimary = widgetQuotePrimary(props);
  const triviaPrimary = widgetTriviaPrimary(props);

  return (
    <OverlapWidget
      clickAction="OPEN_URI"
      clickActionData={{ uri: widgetFaceDeepLinkUrl(face, props.quoteId, props.eventId, props.triviaId) }}
      accessibilityLabel="QueenZone on this day widget"
      style={{
        height: 'match_parent',
        width: 'match_parent',
        backgroundColor: cardBackground,
        borderRadius: 16,
        overflow: 'hidden',
      }}
    >
      <FlexWidget
        style={{
          height: 'match_parent',
          width: 'match_parent',
          justifyContent: 'flex-end',
          alignItems: 'flex-end',
          padding: 4,
        }}
      >
        <ImageWidget
          image={require('../../assets/archive/crest-widget-watermark.png')}
          imageWidth={120}
          imageHeight={120}
          resizeMode="contain"
        />
      </FlexWidget>
      <FlexWidget
        style={{
          height: 'match_parent',
          width: 'match_parent',
          flexDirection: 'column',
          justifyContent: 'flex-start',
          padding: 14,
        }}
      >
        {face ? (
          <TextWidget
            text={widgetEyebrow(face)}
            style={{ fontSize: 10, fontWeight: '600', color: gold, marginBottom: 6 }}
          />
        ) : (
          <TextWidget
            text="ON THIS DAY"
            style={{ fontSize: 10, fontWeight: '600', color: gold, marginBottom: 6 }}
          />
        )}
        {face === 'day' ? (
          <TextWidget
            text={dayPrimary}
            style={{
              fontSize: widgetPrimaryFontSize(dayPrimary, family),
              color: cream,
              width: 'match_parent',
            }}
            maxLines={WIDGET_QUOTE_MAX_LINES}
          />
        ) : null}
        {face === 'day' ? (
          <TextWidget
            text={widgetDaySecondary(props)}
            style={{ fontSize: secondarySize, color: mutedCream, width: 'match_parent' }}
            maxLines={WIDGET_QUOTE_SECONDARY_MAX_LINES}
          />
        ) : null}
        {face === 'quote' ? (
          <TextWidget
            text={quotePrimary}
            style={{
              fontSize: widgetPrimaryFontSize(quotePrimary, family),
              color: mutedCream,
              width: 'match_parent',
            }}
            maxLines={WIDGET_QUOTE_MAX_LINES}
          />
        ) : null}
        {face === 'quote' ? (
          <TextWidget
            text={widgetQuoteSecondary(props)}
            style={{ fontSize: secondarySize, color: mutedCream, width: 'match_parent' }}
            maxLines={WIDGET_QUOTE_SECONDARY_MAX_LINES}
          />
        ) : null}
        {face === 'trivia' ? (
          <TextWidget
            text={triviaPrimary}
            style={{
              fontSize: widgetPrimaryFontSize(triviaPrimary, family),
              color: cream,
              width: 'match_parent',
            }}
            maxLines={WIDGET_QUOTE_MAX_LINES}
          />
        ) : null}
        {face == null ? (
          <TextWidget
            text={widgetEmptyText}
            style={{
              fontSize: widgetPrimaryFontSize(widgetEmptyText, family),
              color: mutedCream,
              width: 'match_parent',
            }}
            maxLines={WIDGET_QUOTE_MAX_LINES}
          />
        ) : null}
      </FlexWidget>
    </OverlapWidget>
  );
}
