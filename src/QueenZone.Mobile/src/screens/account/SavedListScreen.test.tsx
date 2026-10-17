import { screen } from '@testing-library/react-native';
import { memberProfileFixture } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { resetDownloadUiForTests } from '../../downloads/uiState';
import { SavedListScreen } from './SavedListScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../audio/FanPerformancePlayer', () => ({
  useFanPerformancePlayer: () => ({
    current: null,
    playing: false,
    play: jest.fn(),
    toggle: jest.fn(),
  }),
}));

describe('SavedListScreen', () => {
  it('shows downloaded performances for the offline library kind', async () => {
    resetDownloadUiForTests();
    mockSession.isSignedIn = true;
    mockSession.profile = memberProfileFixture({ memberId: 'member-1' });
    await renderWithProviders(
      <SavedListScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'saved', name: 'SavedList', params: { kind: 'offline' } } as never}
      />,
    );
    expect(screen.getByTestId(testIds.fanPerformanceDownloadsScreen)).toBeOnTheScreen();
    expect(screen.getByText('No downloaded recordings yet.')).toBeOnTheScreen();
  });
});
