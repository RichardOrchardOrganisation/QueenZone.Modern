import { screen } from '@testing-library/react-native';
import { renderWithProviders } from '../../test/render';
import { AboutArchiveScreen } from './AboutArchiveScreen';

describe('AboutArchiveScreen', () => {
  it('shows the restored archive account and sign-off', () => {
    renderWithProviders(<AboutArchiveScreen />, { navigation: false });

    expect(screen.getByText(/This is a companion app for Queenzone\.org\./)).toBeOnTheScreen();
    expect(screen.getByText(/Richard's Queen Page/)).toBeOnTheScreen();
    expect(screen.getByText(/The original site was retired in 2020\./)).toBeOnTheScreen();
    expect(screen.getByText(/Richard Orchard/)).toBeOnTheScreen();
    expect(screen.getByText(/www\.richardorchard\.com/)).toBeOnTheScreen();
  });
});
