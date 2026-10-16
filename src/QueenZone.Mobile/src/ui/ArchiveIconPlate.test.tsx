import { allComponentProps } from '../test/renderer';
import Svg, { G } from 'react-native-svg';
import { renderWithProviders } from '../test/render';
import { ArchiveIconPlate } from './ArchiveIconPlate';

describe('ArchiveIconPlate', () => {
  it('centres the 24-space glyph with a group transform instead of a nested Svg', async () => {
    const size = 64;
    const glyphSize = Math.round(size * 0.4375);
    const s = glyphSize / 24;
    const tx = (size - glyphSize) / 2;

    await renderWithProviders(
      <ArchiveIconPlate name="articles" size={size} />,
      { navigation: false },
    );

    const svgs = allComponentProps(Svg);
    expect(svgs).toHaveLength(1);
    expect(svgs[0].props).toMatchObject({
      width: size,
      height: size,
      viewBox: `0 0 ${size} ${size}`,
      accessibilityElementsHidden: true,
      importantForAccessibility: 'no-hide-descendants',
      'aria-hidden': true,
    });

    const glyph = allComponentProps(G).find(
      (node) =>
        typeof node.props.transform === 'string' && node.props.transform.startsWith('translate('),
    );
    expect(glyph).toBeDefined();
    expect(glyph?.props.transform).toBe(`translate(${tx}, ${tx}) scale(${s})`);
    expect(glyph?.props.fill).toBe('none');
    expect(glyph?.props.strokeWidth).toBe(1.15);
    expect(glyph?.props.strokeLinecap).toBe('round');
    expect(glyph?.props.strokeLinejoin).toBe('round');
  });
});
