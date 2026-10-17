import { render, screen } from '@testing-library/react-native';
import { archiveDisclaimer } from '../theme/tokens';
import { BootSplash } from './BootSplash';

describe('BootSplash', () => {
  it('renders the wordmark, tagline, and archive disclaimer', async () => {
    await render(<BootSplash fontsReady={false} fadingOut={false} />);

    expect(screen.getByText('Queenzone')).toBeTruthy();
    expect(screen.getByText('The Queenzone.com Archive')).toBeTruthy();
    expect(screen.getByText(archiveDisclaimer)).toBeTruthy();
  });

  it('renders while fading out without throwing', async () => {
    await render(<BootSplash fontsReady fadingOut />);

    expect(screen.getByTestId('boot-splash')).toBeTruthy();
  });
});
