import { Alert, Dimensions, Image, Linking } from 'react-native';
import { act, screen, userEvent } from '@testing-library/react-native';
import RenderHTML from '@native-html/render';
import { renderWithProviders } from '../test/render';
import { dark, fonts, light, type } from '../theme';
import { RichHtmlBody } from './RichHtmlBody';

const mockAppConfig = {
  appEnv: 'development' as const,
  apiBaseUrl: 'http://qz.test',
  version: '0.1.0',
};

const prepareNewsHtmlControl = { passthrough: false };

jest.mock('../config', () => ({
  getAppConfig: () => mockAppConfig,
}));

jest.mock('./html/prepareNewsHtml', () => {
  const actual = jest.requireActual('./html/prepareNewsHtml') as {
    prepareNewsHtml: (html: string | null | undefined) => string;
  };
  return {
    prepareNewsHtml: (html: string | null | undefined) =>
      prepareNewsHtmlControl.passthrough ? (html ?? '').trim() : actual.prepareNewsHtml(html),
  };
});

jest.mock('@native-html/render', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const React = require('react') as typeof import('react');
  const actual = jest.requireActual('@native-html/render') as {
    default: typeof RenderHTML;
  };
  const MockRenderHTML = jest.fn((props: React.ComponentProps<typeof actual.default>) =>
    React.createElement(actual.default, props),
  );
  return {
    ...actual,
    __esModule: true,
    default: MockRenderHTML,
  };
});

/** QueenZone news/article markup the renderer must keep after TRE 12 / htmlparser2 10. */
const queenZoneBody = [
  '<h2>A Night at the Opera</h2>',
  '<h3>Rockfield sessions</h3>',
  '<p>The restored article records six weeks at <strong>Rockfield</strong> and <em>Sarm</em>.</p>',
  '<p>Read the <a href="https://www.queenzone.org/news/42">source note</a> and the ',
  '<a href="https://www.youtube.com/watch?v=1GfZoSuG8WY">video</a>.</p>',
  "<blockquote>We're just four people who play together.</blockquote>",
  '<ul><li>Bohemian Rhapsody</li><li>You&#39;re My Best Friend</li></ul>',
  '<ol><li>Record</li><li>Mix</li></ol>',
  '<p><img src="/ugc/news/sample-crest.jpg" alt="Queen crest"></p>',
].join('');

const ignoredDomTags = ['iframe', 'script', 'object', 'embed', 'form', 'video', 'audio', 'svg'];

function renderBody(
  html: string,
  options: { horizontalInset?: number; themePreference?: 'dark' | 'light' } = {},
) {
  return renderWithProviders(
    <RichHtmlBody html={html} horizontalInset={options.horizontalInset} />,
    { navigation: false, themePreference: options.themePreference ?? 'dark' },
  );
}

function htmlRenderer() {
  return screen.UNSAFE_getByType(RenderHTML);
}

describe('RichHtmlBody', () => {
  beforeEach(() => {
    prepareNewsHtmlControl.passthrough = false;
    jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    jest.spyOn(Alert, 'alert');
    // RN 0.86 Image.getSize is promise-based; jest-expo still mocks the
    // callback ImageLoader, which throws and then updates IMGElement outside act.
    jest.spyOn(Image, 'getSize').mockImplementation(((
      _uri: string,
      success?: (width: number, height: number) => void,
    ) => {
      success?.(320, 240);
      return Promise.resolve({ width: 320, height: 240 });
    }) as typeof Image.getSize);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  describe('rendering', () => {
    it('renders QueenZone long-form tags through the native HTML engine', async () => {
      renderBody(queenZoneBody);

      expect(screen.getByText('A Night at the Opera')).toBeOnTheScreen();
      expect(screen.getByText('Rockfield sessions')).toBeOnTheScreen();
      expect(screen.getByText(/The restored article records six weeks/)).toBeOnTheScreen();
      expect(screen.getByText('Rockfield')).toBeOnTheScreen();
      expect(screen.getByText('Sarm')).toBeOnTheScreen();
      expect(screen.getByText('source note')).toBeOnTheScreen();
      expect(screen.getByText('video')).toBeOnTheScreen();
      expect(screen.getByText("We're just four people who play together.")).toBeOnTheScreen();
      expect(screen.getByText('Bohemian Rhapsody')).toBeOnTheScreen();
      expect(screen.getByText("You're My Best Friend")).toBeOnTheScreen();
      expect(screen.getByText('Record')).toBeOnTheScreen();
      expect(screen.getByText('Mix')).toBeOnTheScreen();
      expect(await screen.findByTestId('image-success')).toBeOnTheScreen();
    });

    it('resolves root-relative UGC images against the API origin', async () => {
      renderBody('<p><img src="/ugc/news/sample-crest.jpg" alt="Queen crest"></p>');

      const image = await screen.findByTestId('image-success');
      expect(image.props.source).toEqual(
        expect.objectContaining({ uri: 'http://qz.test/ugc/news/sample-crest.jpg' }),
      );
      expect(htmlRenderer().props.source.baseUrl).toBe('http://qz.test');
    });

    it('renders forum-style breaks and leaves double-encoded entities as text', () => {
      renderBody('<p>Hello<br>World</p><p>5 &amp;lt; 10</p>');

      expect(screen.getByText(/Hello/)).toBeOnTheScreen();
      expect(screen.getByText(/World/)).toBeOnTheScreen();
      expect(screen.getByText(/5/)).toBeOnTheScreen();
      expect(screen.queryByText('<10')).toBeNull();
    });

    it('renders nothing when the prepared body is empty', () => {
      renderBody('   ');

      expect(screen.UNSAFE_queryByType(RenderHTML)).toBeNull();
    });

    it('sizes the engine to the parent inset and keeps long-form text props', () => {
      renderBody('<p>Inset probe</p>', { horizontalInset: 40 });

      const { width } = Dimensions.get('window');
      const renderer = htmlRenderer();
      expect(renderer.props.contentWidth).toBe(Math.max(width - 80, 120));
      expect(renderer.props.defaultTextProps).toEqual({
        allowFontScaling: true,
        maxFontSizeMultiplier: 1.4,
      });
      expect(renderer.props.systemFonts).toEqual(
        expect.arrayContaining([fonts.body, fonts.bodyMedium, fonts.bodySemi, fonts.display]),
      );
      expect(renderer.props.enableExperimentalMarginCollapsing).toBe(true);
    });
  });

  describe('security', () => {
    it('strips unsupported embed tags before they reach the engine', () => {
      renderBody(
        '<p>Before</p><iframe src="https://evil.example"></iframe><p>After</p><script>alert(1)</script>',
      );

      expect(screen.getByText('Before')).toBeOnTheScreen();
      expect(screen.getByText('After')).toBeOnTheScreen();
      expect(htmlRenderer().props.source.html).not.toMatch(/<\/?(iframe|script)\b/i);
    });

    it('passes ignoredDomTags as defence in depth for leftovers the stripper misses', () => {
      renderBody('<p>Queen news</p>');

      expect(htmlRenderer().props.ignoredDomTags).toEqual(ignoredDomTags);
    });

    it('drops leftover ignored tags in the upgraded parser when the stripper is bypassed', () => {
      prepareNewsHtmlControl.passthrough = true;
      renderBody(
        '<p>Safe</p><iframe src="https://evil.example">tracker</iframe><script>document.cookie</script><svg><text>vector</text></svg>',
      );

      expect(htmlRenderer().props.source.html).toMatch(/iframe|script|svg/i);
      expect(htmlRenderer().props.ignoredDomTags).toEqual(ignoredDomTags);
      expect(screen.getByText('Safe')).toBeOnTheScreen();
      expect(screen.queryByText('tracker')).toBeNull();
      expect(screen.queryByText('document.cookie')).toBeNull();
      expect(screen.queryByText('vector')).toBeNull();
    });

    it('does not execute event-handler attributes or javascript: image sources', async () => {
      renderBody(
        '<p onclick="alert(1)">Queen news</p>' +
          '<img src="javascript:alert(1)" alt="bad crest" onerror="alert(1)">' +
          '<a href="javascript:alert(1)">Click me</a>',
      );

      await act(async () => {
        await Promise.resolve();
      });

      expect(screen.getByText('Queen news')).toBeOnTheScreen();
      expect(screen.getByText('Click me')).toBeOnTheScreen();
      expect(Alert.alert).not.toHaveBeenCalled();
      expect(Linking.openURL).not.toHaveBeenCalled();
    });
  });

  describe('link handling', () => {
    it('opens https links in the system browser', async () => {
      renderBody('<p><a href="https://www.youtube.com/watch?v=1GfZoSuG8WY">Watch</a></p>');

      const user = userEvent.setup();
      await user.press(screen.getByText('Watch'));

      expect(Linking.openURL).toHaveBeenCalledWith('https://www.youtube.com/watch?v=1GfZoSuG8WY');
      expect(Alert.alert).not.toHaveBeenCalled();
    });

    it('opens http links and ignores javascript and data hrefs', async () => {
      renderBody(
        '<p><a href="http://qz.test/news/42">Http</a> ' +
          '<a href="javascript:alert(1)">Js</a> ' +
          '<a href="data:text/html,<script>alert(1)</script>">Data</a></p>',
      );

      const user = userEvent.setup();
      await user.press(screen.getByText('Http'));
      await user.press(screen.getByText('Js'));
      await user.press(screen.getByText('Data'));

      expect(Linking.openURL).toHaveBeenCalledTimes(1);
      expect(Linking.openURL).toHaveBeenCalledWith('http://qz.test/news/42');
      expect(Alert.alert).not.toHaveBeenCalled();
    });

    it('resolves root-relative archive links against the API origin', async () => {
      renderBody('<p><a href="/news/42">Relative</a></p>');

      const user = userEvent.setup();
      await user.press(screen.getByText('Relative'));

      expect(Linking.openURL).toHaveBeenCalledWith('http://qz.test/news/42');
      expect(Alert.alert).not.toHaveBeenCalled();
    });
  });

  describe('theming', () => {
    it.each([
      ['dark', dark] as const,
      ['light', light] as const,
    ])('maps body and link styles from the %s theme', (preference, colors) => {
      renderBody('<p>Body copy <a href="https://www.queenzone.org/news/42">Archive link</a></p>', {
        themePreference: preference,
      });

      const { tagsStyles, baseStyle } = htmlRenderer().props;
      expect(baseStyle).toEqual(
        expect.objectContaining({
          color: colors.textPrimary,
          fontFamily: fonts.body,
          fontSize: type.longform.fontSize,
          lineHeight: type.longform.lineHeight,
        }),
      );
      expect(tagsStyles.body).toEqual(
        expect.objectContaining({
          color: colors.textPrimary,
          fontFamily: fonts.body,
        }),
      );
      expect(tagsStyles.a).toEqual(
        expect.objectContaining({
          color: colors.accentPrimary,
          textDecorationLine: 'underline',
        }),
      );
      expect(tagsStyles.blockquote).toEqual(
        expect.objectContaining({
          borderLeftColor: colors.accentEditorial,
          color: colors.textSecondary,
        }),
      );
      expect(tagsStyles.h2).toEqual(expect.objectContaining({ fontFamily: fonts.display }));

      expect(screen.getByText('Archive link')).toHaveStyle({
        color: colors.accentPrimary,
        textDecorationLine: 'underline',
      });
    });
  });

  describe('error boundary', () => {
    it('falls back to plain text when the renderer throws', () => {
      const MockRenderHTML = RenderHTML as jest.MockedFunction<typeof RenderHTML>;
      MockRenderHTML.mockImplementationOnce(() => {
        throw new Error('renderer failed');
      });

      renderBody('<p>The restored article body.</p>');

      expect(screen.getByText('The restored article body.')).toBeOnTheScreen();
      expect(screen.queryByText('<p>')).toBeNull();
    });
  });
});
