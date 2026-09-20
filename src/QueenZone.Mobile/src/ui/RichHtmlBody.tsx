import { Component, type ErrorInfo, type ReactNode, useMemo } from 'react';
import {
  Text,
  useWindowDimensions,
  type GestureResponderEvent,
} from 'react-native';
import RenderHTML, {
  defaultSystemFonts,
  type MixedStyleRecord,
} from 'react-native-render-html';
import { toPlainText } from '../api/text';
import { getAppConfig } from '../config';
import { fonts, space, type, useTheme } from '../theme';
import { prepareNewsHtml } from './html/prepareNewsHtml';
import { isHttpUrl } from './html/resolveContentUrl';
import { openExternalUrl } from './openExternalUrl';

type Props = {
  html: string;
  /** Horizontal inset already applied by the parent ScrollView. */
  horizontalInset?: number;
};

type BoundaryProps = {
  fallback: ReactNode;
  children: ReactNode;
};

type BoundaryState = { hasError: boolean };

/** Catches renderer failures and falls back to plain text (#728). */
class HtmlRenderErrorBoundary extends Component<BoundaryProps, BoundaryState> {
  state: BoundaryState = { hasError: false };

  static getDerivedStateFromError(): BoundaryState {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    if (__DEV__) {
      console.warn('RichHtmlBody render failed; using plain text.', error, info.componentStack);
    }
  }

  render(): ReactNode {
    if (this.state.hasError) {
      return this.props.fallback;
    }
    return this.props.children;
  }
}

const systemFonts = [...defaultSystemFonts, fonts.body, fonts.bodyMedium, fonts.bodySemi, fonts.display];

export function RichHtmlBody({ html, horizontalInset = 26 }: Props) {
  const { c } = useTheme();
  const { width } = useWindowDimensions();
  const contentWidth = Math.max(width - horizontalInset * 2, 120);
  const prepared = useMemo(() => prepareNewsHtml(html), [html]);
  const baseUrl = getAppConfig().apiBaseUrl;
  const plainFallback = toPlainText(html);

  const tagsStyles = useMemo<MixedStyleRecord>(
    () => ({
      body: {
        color: c.textPrimary,
        fontFamily: fonts.body,
        fontSize: type.longform.fontSize,
        lineHeight: type.longform.lineHeight,
      },
      p: {
        marginTop: 0,
        marginBottom: space.lg,
      },
      strong: { fontFamily: fonts.bodySemi, fontWeight: '600' },
      b: { fontFamily: fonts.bodySemi, fontWeight: '600' },
      em: { fontStyle: 'italic' },
      i: { fontStyle: 'italic' },
      a: {
        color: c.accentPrimary,
        textDecorationLine: 'underline',
      },
      h2: {
        fontFamily: fonts.display,
        fontSize: 24,
        lineHeight: 28,
        marginTop: space.md,
        marginBottom: space.md,
        color: c.textPrimary,
      },
      h3: {
        fontFamily: fonts.display,
        fontSize: 21,
        lineHeight: 25,
        marginTop: space.md,
        marginBottom: space.sm,
        color: c.textPrimary,
      },
      h4: {
        fontFamily: fonts.display,
        fontSize: 18,
        lineHeight: 22,
        marginTop: space.md,
        marginBottom: space.sm,
        color: c.textPrimary,
      },
      blockquote: {
        borderLeftWidth: 3,
        borderLeftColor: c.accentEditorial,
        paddingLeft: space.md,
        marginVertical: space.lg,
        color: c.textSecondary,
      },
      ul: { marginBottom: space.lg, paddingLeft: space.md },
      ol: { marginBottom: space.lg, paddingLeft: space.md },
      li: { marginBottom: space.sm },
      img: {
        marginVertical: space.lg,
      },
    }),
    [c],
  );

  const renderersProps = useMemo(
    () => ({
      a: {
        onPress: (_event: GestureResponderEvent, href: string) => {
          if (isHttpUrl(href)) {
            void openExternalUrl(href);
          }
        },
      },
      img: {
        enableExperimentalPercentWidth: true,
      },
    }),
    [],
  );

  if (!prepared) {
    return null;
  }

  const fallback = (
    <Text style={[type.longform, { color: c.textPrimary }]} allowFontScaling>
      {plainFallback}
    </Text>
  );

  return (
    <HtmlRenderErrorBoundary fallback={fallback}>
      <RenderHTML
        contentWidth={contentWidth}
        source={{ html: prepared, baseUrl }}
        baseStyle={{
          color: c.textPrimary,
          fontFamily: fonts.body,
          fontSize: type.longform.fontSize,
          lineHeight: type.longform.lineHeight,
        }}
        tagsStyles={tagsStyles}
        systemFonts={systemFonts}
        renderersProps={renderersProps}
        defaultTextProps={{
          allowFontScaling: true,
          maxFontSizeMultiplier: 1.4,
        }}
        enableExperimentalMarginCollapsing
        // Defence in depth: server strips these; ignore if any remain.
        ignoredDomTags={['iframe', 'script', 'object', 'embed', 'form', 'video', 'audio', 'svg']}
      />
    </HtmlRenderErrorBoundary>
  );
}
