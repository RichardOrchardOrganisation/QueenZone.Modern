/**
 * Queenzone — React Native theme tokens (#792).
 *
 * Sourced from the web design system (not eyeballed):
 * - `src/QueenZone.Web/wwwroot/design-system/tokens/colors.css`
 * - `…/typography.css`, `spacing.css`, `fonts.css`
 *
 * Mobile type sizes, dark surfaces, and section rhythm follow the approved
 * handoff at `design/Queenzone mobile app design/handoff/theme.ts` and
 * `STYLE_GUIDE.md`. Palette hex values match the CSS custom properties 1:1.
 *
 * ~90% monochrome. Accent colour carries MEANING, never decoration.
 * On dark, Antique Gold is the link/active/CTA colour (Royal Blue fails contrast).
 */

export const palette = {
  // --qz-* monochrome foundation (colors.css)
  white: '#FFFFFF',
  warmWhite: '#F7F6F3',
  greyLight: '#E8E8E8',
  charcoal: '#2B2B2B',
  black: '#111111',

  grey50: '#FBFBFA',
  grey100: '#F2F1ED',
  grey200: '#E8E8E8',
  grey300: '#D6D6D2',
  grey400: '#B4B4AF',
  grey500: '#8A8A85',
  grey600: '#5F5F5B',
  grey700: '#3D3D3B',
  grey800: '#2B2B2B',
  grey900: '#1A1A1A',

  // --qz-* accents
  blue: '#244A8F',
  blueDeep: '#1B3A72',
  blueTint: '#ECF0F7',
  purple: '#5D3A8A',
  purpleDeep: '#492C6E',
  purpleTint: '#F0ECF5',
  burgundy: '#6B1F33',
  burgundyDeep: '#551828',
  burgundyTint: '#F6ECEE',
  gold: '#B89A4A',
  goldDeep: '#9C8038',
  goldTint: '#F6F1E3',
  /** Slightly lifted gold for dark-theme hover/press affordances (handoff). */
  goldLift: '#D3B868',
} as const;

/** Dark theme — the app default (`surface-inverse` / --qz-black as page). */
export const dark = {
  surfacePage: '#111111',
  surfaceRaised: '#161616',
  surfaceCard: '#1A1A1A',
  surfaceSheet: '#1E1E1E',
  surfaceScrim: 'rgba(0,0,0,0.50)',
  surfaceBarBlur: 'rgba(17,17,17,0.90)',
  surfaceThread: '#0C0C0C',
  bubbleOutgoing: '#171717',

  textPrimary: '#FFFFFF',
  textSecondary: 'rgba(255,255,255,0.66)',
  textMuted: 'rgba(255,255,255,0.50)',
  textOnAccent: '#111111',

  hairline: 'rgba(255,255,255,0.12)',
  border: 'rgba(255,255,255,0.16)',
  borderStrong: 'rgba(255,255,255,0.28)',
  ruleSubtle: 'rgba(255,255,255,0.14)',

  /** On dark, GOLD replaces Royal Blue for links/active state (contrast). */
  accentPrimary: palette.gold,
  accentPress: palette.goldDeep,
  accentTintWeak: 'rgba(184,154,74,0.20)',
  accentArchive: palette.purple,
  accentEditorial: palette.burgundy,
  accentSpecial: palette.gold,
  danger: '#D98A8A',
  success: '#6EE7B7',
  successTint: 'rgba(110,231,183,0.16)',
  successBorder: 'rgba(110,231,183,0.50)',

  crest: 'crest-white.png',
  crestWatermarkOpacity: 0.06,

  /** Archive section icon plate (#1321 handoff) — engraved-line glyph on a raised chip. */
  iconPlateGradient: ['#1C1C1C', '#141414', '#101010'] as [string, string, string],
  iconPlateBorder: 'rgba(255,255,255,0.14)',
  glyphStroke: 'rgba(255,255,255,0.88)',
} as const;

/** Light theme — parity with web semantic aliases in colors.css. */
export const light = {
  surfacePage: palette.white,
  surfaceRaised: palette.warmWhite,
  surfaceCard: palette.white,
  surfaceSheet: palette.white,
  surfaceScrim: 'rgba(17,17,17,0.62)',
  surfaceBarBlur: 'rgba(255,255,255,0.90)',
  surfaceThread: palette.grey50,
  bubbleOutgoing: palette.grey100,

  textPrimary: palette.charcoal,
  textSecondary: palette.grey600,
  textMuted: palette.grey500,
  textOnAccent: palette.white,

  hairline: palette.grey200,
  border: palette.grey200,
  borderStrong: palette.grey300,
  ruleSubtle: palette.grey200,

  accentPrimary: palette.blue,
  accentPress: palette.blueDeep,
  accentTintWeak: palette.blueTint,
  accentArchive: palette.purple,
  accentEditorial: palette.burgundy,
  accentSpecial: palette.gold,
  danger: '#8E2F2F',
  success: '#287A58',
  successTint: 'rgba(40,122,88,0.12)',
  successBorder: 'rgba(40,122,88,0.42)',

  crest: 'crest-black.png',
  crestWatermarkOpacity: 0.05,

  /** Archive section icon plate (#1321 handoff) — light-theme counterpart. */
  iconPlateGradient: [palette.grey100, palette.grey50, palette.white] as [string, string, string],
  iconPlateBorder: palette.grey300,
  glyphStroke: palette.grey700,
} as const;

export type ColorScheme = typeof dark | typeof light;
export type ThemeMode = 'dark' | 'light';

/**
 * Font families — match --font-display / --font-body / --font-titling.
 * Loaded at startup via `useQueenzoneFonts` (Google Font TTFs, same faces as
 * the web WOFF2s). Do not fall back to system serif for display.
 */
export const fonts = {
  display: 'CormorantGaramond-Medium',
  displaySemi: 'CormorantGaramond-SemiBold',
  body: 'Inter-Regular',
  bodyMedium: 'Inter-Medium',
  bodySemi: 'Inter-SemiBold',
  titling: 'Cinzel-Regular',
} as const;

/**
 * Type scale — mobile points/dp (handoff), same three typefaces as typography.css.
 * Cormorant carries hierarchy; Inter reading/UI; Cinzel uppercase kickers only.
 */
export const type = {
  heroTitle: { fontFamily: fonts.display, fontSize: 38, lineHeight: 40, letterSpacing: -0.6 },
  pageTitle: { fontFamily: fonts.display, fontSize: 34, lineHeight: 36, letterSpacing: -0.5 },
  articleTitle: { fontFamily: fonts.display, fontSize: 28, lineHeight: 32, letterSpacing: -0.35 },
  cardTitle: { fontFamily: fonts.display, fontSize: 21, lineHeight: 25, letterSpacing: -0.2 },
  pullQuote: { fontFamily: fonts.display, fontSize: 26, lineHeight: 33 },
  dropCap: { fontFamily: fonts.display, fontSize: 62, lineHeight: 51 },

  standfirst: { fontFamily: fonts.body, fontSize: 18, lineHeight: 29 },
  longform: { fontFamily: fonts.body, fontSize: 18, lineHeight: 31 },
  body: { fontFamily: fonts.body, fontSize: 16, lineHeight: 26 },
  listTitle: { fontFamily: fonts.bodyMedium, fontSize: 15.5, lineHeight: 21 },
  caption: { fontFamily: fonts.body, fontSize: 13, lineHeight: 20 },
  meta: {
    fontFamily: fonts.bodyMedium,
    fontSize: 10.5,
    lineHeight: 15,
    letterSpacing: 0.85,
    textTransform: 'uppercase' as const,
  },

  eyebrow: {
    fontFamily: fonts.titling,
    fontSize: 10,
    lineHeight: 14,
    letterSpacing: 2.2,
    textTransform: 'uppercase' as const,
  },
  numeral: { fontFamily: fonts.titling, fontSize: 26, lineHeight: 30, letterSpacing: 1.6 },
  button: {
    fontFamily: fonts.bodyMedium,
    fontSize: 12,
    lineHeight: 16,
    letterSpacing: 1.2,
    textTransform: 'uppercase' as const,
  },
} as const;

/**
 * Spacing — 4px base matching --space-1…--space-5; xl = --gutter (24).
 * xxl / section are mobile section rhythm from STYLE_GUIDE (34 / 44).
 * avatar / thumb / card are component sizes — do not overload xxl.
 */
export const space = {
  xs: 4, // --space-1
  sm: 8, // --space-2
  md: 12, // --space-3
  base: 16, // --space-4
  lg: 20,
  xl: 24, // --space-5 / --gutter
  xxl: 34, // section header offset
  section: 44, // before footer
  avatar: 34,
  thumb: 64,
  card: 148,
} as const;

/** Radii — --radius-xs/sm/md/pill from spacing.css; sheet/fab from handoff chrome. */
export const radius = {
  xs: 2,
  sm: 3,
  md: 4,
  pill: 999,
  fab: 18,
  sheet: 20,
  sheetIos: 14,
  avatar: 17,
} as const;

export const shadow = {
  card: {
    shadowColor: '#000',
    shadowOpacity: 0.35,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 4 },
    elevation: 3,
  },
  lift: {
    shadowColor: '#000',
    shadowOpacity: 0.45,
    shadowRadius: 18,
    shadowOffset: { width: 0, height: 8 },
    elevation: 6,
  },
  fab: {
    shadowColor: '#000',
    shadowOpacity: 0.5,
    shadowRadius: 20,
    shadowOffset: { width: 0, height: 8 },
    elevation: 8,
  },
} as const;

/** Motion — --dur-fast/base/slow and --ease-out from spacing.css. */
export const motion = {
  fast: 180,
  base: 320,
  slow: 620,
  easing: [0.22, 0.61, 0.36, 1] as const,
} as const;

/** Platform chrome — the only intended iOS/Android divergence (SPEC §5). */
export const chrome = {
  ios: {
    statusBarHeight: 47,
    navBarHeight: 44,
    navTitle: { fontFamily: fonts.bodySemi, fontSize: 17, letterSpacing: -0.2 },
    navTitleAlign: 'center' as const,
    backAffordance: 'chevron+label' as const,
    tabBarHeight: 83,
    tabIcon: 25,
    tabLabel: 10,
    tabActiveStyle: 'tint' as const,
    sheet: 'actionSheet' as const,
    searchFieldRadius: radius.md + 6,
    pressFeedback: 'opacity' as const,
  },
  android: {
    statusBarHeight: 32,
    navBarHeight: 56,
    navTitle: { fontFamily: fonts.bodyMedium, fontSize: 20, letterSpacing: 0.1 },
    navTitleAlign: 'left' as const,
    backAffordance: 'arrow' as const,
    tabBarHeight: 80,
    tabIcon: 24,
    tabLabel: 11.5,
    tabActiveStyle: 'pill' as const,
    sheet: 'bottomSheet' as const,
    searchFieldRadius: radius.pill,
    pressFeedback: 'ripple' as const,
    fabSize: 58,
  },
} as const;

/** Imagery: archival photography renders monochrome (SPEC §2.6). */
export const imagery = {
  grayscale: true,
  contrast: 1.05,
  scrimBottom: ['rgba(17,17,17,0.55)', 'rgba(17,17,17,0)', 'rgba(17,17,17,0.82)', '#111111'],
  scrimStops: [0, 0.32, 0.74, 1],
} as const;

export const archiveDisclaimer =
  'An independent fan archive. Not affiliated with Queen or its representatives.';

export const theme = {
  palette,
  dark,
  light,
  fonts,
  type,
  space,
  radius,
  shadow,
  motion,
  chrome,
  imagery,
  archiveDisclaimer,
} as const;

export type Theme = typeof theme;
export default theme;
