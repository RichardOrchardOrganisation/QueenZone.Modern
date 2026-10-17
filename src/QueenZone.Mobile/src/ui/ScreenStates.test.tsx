import { screen, userEvent } from '@testing-library/react-native';
import { EmptyBlock, ErrorBlock, LoadingBlock, OfflineBanner, formatOfflineUpdatedLabel } from './ScreenStates';
import { renderWithProviders } from '../test/render';
import { testIds } from '../test/testIds';

describe('ScreenStates', () => {
  it('exposes a labelled loading progressbar', async () => {
    await renderWithProviders(<LoadingBlock label="Loading news…" />, { navigation: false });
    expect(screen.getByLabelText('Loading news…')).toBeOnTheScreen();
  });

  it('shows error copy and invokes retry', async () => {
    const user = userEvent.setup();
    const onRetry = jest.fn();
    await renderWithProviders(
      <ErrorBlock message="The server had a problem. Try again shortly." onRetry={onRetry} />,
      { navigation: false },
    );
    expect(screen.getByText('Unable to load')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('renders empty copy', async () => {
    await renderWithProviders(<EmptyBlock message="No news articles yet." />, { navigation: false });
    expect(screen.getByText('No news articles yet.')).toBeOnTheScreen();
  });

  it('formats and renders the offline snapshot banner', async () => {
    expect(formatOfflineUpdatedLabel('2024-06-01T10:00:00.000Z')).toBe(
      'Offline · last updated 2024-06-01 10:00 UTC',
    );
    expect(formatOfflineUpdatedLabel(null)).toBe('Offline');
    await renderWithProviders(<OfflineBanner cachedAt="2024-06-01T10:00:00.000Z" testID={testIds.offlineBanner} />, {
      navigation: false,
    });
    expect(screen.getByTestId(testIds.offlineBanner)).toBeOnTheScreen();
    expect(screen.getByText('Offline · last updated 2024-06-01 10:00 UTC')).toBeOnTheScreen();
  });
});
