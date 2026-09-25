import { View } from 'react-native';
import { screen } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { dark, imagery, light } from '../theme';
import { HeroFeature } from './HeroFeature';

jest.mock('expo-linear-gradient', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View } = require('react-native');
  return { LinearGradient: View };
});

const item = {
  kicker: 'Lead story',
  title: 'QueenZone modernisation begins',
  standfirst: 'The archive rebuild ships its first public news slice.',
  meta: ['News', '4 min'],
  image: { uri: 'https://cdn.queenzone.org/news/hero.jpg' },
};

function renderHero(preference: 'light' | 'dark') {
  renderWithProviders(<HeroFeature item={item} onPress={jest.fn()} />, {
    navigation: false,
    themePreference: preference,
  });
}

describe('HeroFeature', () => {
  it.each(['light', 'dark'] as const)(
    'paints on-image light text over the existing dark scrim in %s mode',
    (preference) => {
      renderHero(preference);

      expect(screen.getByText('Lead story')).toHaveStyle({ color: dark.textPrimary });
      expect(screen.getByText(item.title)).toHaveStyle({ color: dark.textPrimary });
      expect(screen.getByText(item.standfirst)).toHaveStyle({ color: dark.textSecondary });
      expect(screen.getByText('NEWS · 4 MIN')).toHaveStyle({ color: dark.textMuted });

      expect(dark.textPrimary).not.toBe(light.textPrimary);
      expect(screen.getByText(item.title)).not.toHaveStyle({ color: light.textPrimary });
      expect(screen.getByText(item.standfirst)).not.toHaveStyle({ color: light.textSecondary });

      const scrim = screen.UNSAFE_getAllByType(View).find((node) => Array.isArray(node.props.colors));
      expect(scrim?.props.colors).toEqual(imagery.scrimBottom);
      expect(scrim?.props.locations).toEqual(imagery.scrimStops);
    },
  );
});
