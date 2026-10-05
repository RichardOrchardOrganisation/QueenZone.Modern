import Svg, { Defs, G, RadialGradient, Rect, Stop } from 'react-native-svg';
import { useTheme } from '../theme';
import { archiveIconShapes, type ArchiveIconName } from './icons/archiveSectionIcons';

type Props = {
  /** Archive section id (see `archiveIconShapes`). */
  name: ArchiveIconName;
  /** Plate side length. Glyph renders at ~44% of this (design handoff sizing rule). */
  size?: number;
  style?: object;
};

/**
 * The 64×64 icon plate that replaces the Archive hub's placeholder thumbnail
 * (#1321 handoff): a dark radial-gradient chip with a centred engraved-line
 * glyph. Purely decorative — hidden from the accessibility tree; the row's
 * title + meta carry the accessible label.
 *
 * Glyphs live in 24×24 space. iOS react-native-svg ignores nested child Svg
 * x/y/width/height, so we translate+scale a group instead (#1344).
 */
export function ArchiveIconPlate({ name, size = 64, style }: Props) {
  const { c, radius } = useTheme();
  const glyphSize = Math.round(size * 0.4375); // 28/64 per handoff
  let strokeWidth = 1.15;
  if (glyphSize <= 20) {
    strokeWidth = 1.3;
  } else if (glyphSize >= 34) {
    strokeWidth = 1.1;
  }
  const s = glyphSize / 24;
  const tx = (size - glyphSize) / 2;
  const ty = tx;
  const gradientId = `archive-icon-plate-${name}`;
  const inset = size * (0.5 / 64);

  return (
    <Svg
      width={size}
      height={size}
      viewBox={`0 0 ${size} ${size}`}
      style={style}
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
      aria-hidden
    >
      <Defs>
        <RadialGradient id={gradientId} cx="50%" cy="0%" r="120%">
          <Stop offset="0%" stopColor={c.iconPlateGradient[0]} />
          <Stop offset="60%" stopColor={c.iconPlateGradient[1]} />
          <Stop offset="100%" stopColor={c.iconPlateGradient[2]} />
        </RadialGradient>
      </Defs>
      <Rect
        x={inset}
        y={inset}
        width={size - inset * 2}
        height={size - inset * 2}
        rx={radius.sm}
        ry={radius.sm}
        fill={`url(#${gradientId})`}
        stroke={c.iconPlateBorder}
        strokeWidth={1}
      />
      <G
        transform={`translate(${tx}, ${ty}) scale(${s})`}
        fill="none"
        stroke={c.glyphStroke}
        strokeWidth={strokeWidth}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        {archiveIconShapes[name]}
      </G>
    </Svg>
  );
}
