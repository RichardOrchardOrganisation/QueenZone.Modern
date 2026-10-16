import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { AnalyticsConsentPrompt } from './AnalyticsConsentPrompt';
import { updateAnalyticsConsent } from './telemetry';

let mockConsent: 'unset' | 'granted' | 'denied' = 'unset';

jest.mock('./consent', () => ({
  useAnalyticsConsent: () => mockConsent,
}));

jest.mock('./telemetry', () => ({
  updateAnalyticsConsent: jest.fn(async () => undefined),
}));

jest.mock('../config/appConfig', () => ({
  getAppConfig: () => ({ telemetryDeckAppId: 'telemetry-app-id' }),
}));

describe('AnalyticsConsentPrompt', () => {
  beforeEach(() => {
    mockConsent = 'unset';
    (updateAnalyticsConsent as jest.Mock).mockClear();
  });

  it('explains the narrow collection and offers an equal refusal', async () => {
    await renderWithProviders(<AnalyticsConsentPrompt />, { navigation: false });

    expect(screen.getByRole('header', { name: 'Anonymous analytics' })).toBeOnTheScreen();
    expect(screen.getByText(/account details, content, searches, precise location/)).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Allow anonymous analytics' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: "Don't allow" })).toBeOnTheScreen();
  });

  it("persists the user's choice", async () => {
    await renderWithProviders(<AnalyticsConsentPrompt />, { navigation: false });

    await fireEvent.press(screen.getByRole('button', { name: "Don't allow" }));
    await waitFor(() => expect(updateAnalyticsConsent).toHaveBeenCalledWith(false));
  });

  it('stays hidden after a decision', async () => {
    mockConsent = 'granted';
    await renderWithProviders(<AnalyticsConsentPrompt />, { navigation: false });
    expect(screen.queryByText('Anonymous analytics')).toBeNull();
  });
});
