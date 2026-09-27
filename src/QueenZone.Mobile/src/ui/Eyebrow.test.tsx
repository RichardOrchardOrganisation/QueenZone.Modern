import { screen } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { dark, light } from '../theme';
import { Eyebrow } from './Eyebrow';

describe('Eyebrow', () => {
  it.each(['light', 'dark'] as const)(
    'keeps onDark as fixed light-on-dark text in %s mode',
    (preference) => {
      renderWithProviders(<Eyebrow tone="onDark">Lead story</Eyebrow>, {
        navigation: false,
        themePreference: preference,
      });

      expect(screen.getByText('Lead story')).toHaveStyle({ color: dark.textPrimary });
      expect(dark.textPrimary).not.toBe(light.textPrimary);
      expect(screen.getByText('Lead story')).not.toHaveStyle({ color: light.textPrimary });
    },
  );

  it('still uses scheme textPrimary for the primary tone', () => {
    renderWithProviders(<Eyebrow tone="primary">In the forum</Eyebrow>, {
      navigation: false,
      themePreference: 'light',
    });

    expect(screen.getByText('In the forum')).toHaveStyle({ color: light.textPrimary });
  });
});
