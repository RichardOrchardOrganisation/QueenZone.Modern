import type { ReactElement } from 'react';
import { Circle, Path, Rect } from 'react-native-svg';

/**
 * Archive section glyphs (#1321 design handoff) — engraved line icons, one per
 * archive destination. Drawn on a 24×24 grid; render at 44% of the icon plate.
 * Geometry copied verbatim from `qz-archive-*.svg` in the handoff bundle.
 */
export const ARCHIVE_ICON_NAMES = [
  'articles',
  'timeline',
  'biography',
  'discography',
  'tribute',
  'fan-performances',
  'restored',
  'trivia',
  'quiz',
  'old-site',
] as const;

export type ArchiveIconName = (typeof ARCHIVE_ICON_NAMES)[number];

/** Shape elements only — caller's <Svg> supplies viewBox, stroke, fill, size. */
export const archiveIconShapes: Record<ArchiveIconName, ReactElement> = {
  articles: (
    <>
      <Rect x={4} y={3.5} width={16} height={17} />
      <Path d="M6.5 7.5h11" />
      <Path d="M6.5 11h4.5M6.5 13.5h4.5M6.5 16h4.5" />
      <Path d="M13 11h4.5M13 13.5h4.5M13 16h4.5" />
    </>
  ),
  timeline: (
    <>
      <Path d="M8 2.5v19" />
      <Path d="M8 4.5l2 2-2 2-2-2z" />
      <Path d="M8 10l2 2-2 2-2-2z" />
      <Path d="M8 15.5l2 2-2 2-2-2z" />
      <Path d="M12 6.5h6.5M12 12h6.5M12 17.5h4.5" />
    </>
  ),
  biography: (
    <>
      <Rect x={5} y={2.5} width={14} height={19} />
      <Path d="M8.5 2.5v19" />
      <Path d="M15.5 2.5v7.5l-2-1.5-2 1.5V2.5" />
    </>
  ),
  discography: (
    <>
      <Circle cx={16.5} cy={12} r={6.2} />
      <Circle cx={16.5} cy={12} r={1.7} />
      <Rect x={2.5} y={4.5} width={13.5} height={15} />
    </>
  ),
  tribute: (
    <>
      <Path d="M12 2.8c2.5 3.3 3.7 5.2 3.7 7.1A3.7 3.7 0 0 1 12 13.6a3.7 3.7 0 0 1-3.7-3.7c0-1.9 1.2-3.8 3.7-7.1z" />
      <Path d="M12 13.6v4.4" />
      <Path d="M8 21h8" />
    </>
  ),
  'fan-performances': (
    <>
      <Circle cx={10} cy={12} r={6.6} />
      <Path d="M8.4 9.3l4.4 2.7-4.4 2.7z" />
      <Path d="M18.8 8.4a5.6 5.6 0 0 1 0 7.2" />
      <Path d="M21.4 6.2a9 9 0 0 1 0 11.6" />
    </>
  ),
  restored: (
    <>
      <Rect x={8} y={8} width={8} height={8} />
      <Path d="M9.6 14.4l2.3-2.3 1.9 1.9" />
      <Circle cx={13.9} cy={10.3} r={0.7} />
      <Path d="M4 12A8 8 0 0 1 18.4 7" />
      <Path d="M4 8.4V12h3.6" />
    </>
  ),
  trivia: (
    <>
      <Rect x={3} y={6.5} width={13.5} height={14.5} />
      <Path d="M7 6.5V3.5h13.5V17" />
      <Path d="M8.2 11.6a1.9 1.9 0 1 1 2.7 1.8c-.7.4-.9.9-.9 1.8" />
      <Path d="M10 18h.01" />
    </>
  ),
  quiz: (
    <>
      <Rect x={3} y={3.5} width={18} height={17} />
      <Circle cx={7.2} cy={8.4} r={1.4} />
      <Path d="M11.5 8.4h8.5" />
      <Path d="M6 12.6l1 1 2-2" />
      <Path d="M11.5 12.6h8.5" />
      <Path d="M11.5 16.8h8.5" />
      <Circle cx={7.2} cy={16.8} r={1.4} />
    </>
  ),
  'old-site': (
    <>
      <Rect x={2.5} y={4} width={19} height={16} />
      <Path d="M2.5 9h19" />
      <Circle cx={5.8} cy={6.5} r={0.7} />
      <Circle cx={8.2} cy={6.5} r={0.7} />
      <Path d="M6 12.5h7M6 15.5h9" />
    </>
  ),
};
