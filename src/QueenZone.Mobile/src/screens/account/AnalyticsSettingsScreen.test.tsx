import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderWithProviders } from '../../test/render';
import { updateAnalyticsConsent } from '../../analytics/telemetry';
import { AnalyticsSettingsScreen } from './AnalyticsSettingsScreen';

let mockConsent: 'loading' | 'unset' | 'granted' | 'denied' = 'granted';

jest.mock('../../analytics/consent', () => ({
  useAnalyticsConsent: () => mockConsent,
}));

jest.mock('../../analytics/telemetry', () => ({
  updateAnalyticsConsent: jest.fn(async () => undefined),
}));

describe('AnalyticsSettingsScreen', () => {
  beforeEach(() => {
    mockConsent = 'granted';
    (updateAnalyticsConsent as jest.Mock).mockClear();
  });

  it('lets signed-in or signed-out users withdraw consent', async () => {
    await renderWithProviders(
      <AnalyticsSettingsScreen navigation={{} as never} route={{ key: 'analytics', name: 'AnalyticsSettings' }} />,
      { navigation: false },
    );

    const toggle = screen.getByRole('switch', { name: 'Share anonymous analytics' });
    expect(toggle).toHaveProp('value', true);
    await fireEvent(toggle, 'valueChange', false);
    await waitFor(() => expect(updateAnalyticsConsent).toHaveBeenCalledWith(false));
  });

  it('disables the control while the preference loads', async () => {
    mockConsent = 'loading';
    await renderWithProviders(
      <AnalyticsSettingsScreen navigation={{} as never} route={{ key: 'analytics', name: 'AnalyticsSettings' }} />,
      { navigation: false },
    );
    expect(screen.getByRole('switch', { name: 'Share anonymous analytics' })).toBeDisabled();
  });
});
