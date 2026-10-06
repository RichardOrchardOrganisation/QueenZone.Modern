import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchJsonWithOfflineCache } from '../../cache';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { createMockSession } from '../../test/mockSession';
import { crosswordFixture } from '../../test/crosswordFixture';
import { CrosswordPlayScreen, CrosswordSolver } from './CrosswordPlayScreen';

const mockSession = createMockSession();
jest.mock('../../session/SessionContext', () => ({ useSession: () => mockSession }));
jest.mock('../../cache', () => ({ fetchJsonWithOfflineCache: jest.fn() }));
const mockNetworkState = { isConnected: true, isInternetReachable: true };
jest.mock('expo-network', () => ({ useNetworkState: () => mockNetworkState }));
jest.mock('expo-haptics', () => ({ selectionAsync: jest.fn().mockResolvedValue(undefined) }));
jest.mock('../../api/crosswords', () => ({
  checkCrossword: jest.fn(), revealCrossword: jest.fn(), completeCrossword: jest.fn(),
  fetchCrosswordProgress: jest.fn().mockResolvedValue(undefined),
}));

const { cellRenders } = jest.requireMock('../../crosswords/CrosswordCell') as { cellRenders: number[] };

jest.mock('../../crosswords/CrosswordCell', () => {
  const React = jest.requireActual('react') as typeof import('react');
  const actual = jest.requireActual('../../crosswords/CrosswordCell') as typeof import('../../crosswords/CrosswordCell');
  const renders: number[] = [];
  function CrosswordCellMock(props: import('../../crosswords/CrosswordCell').CrosswordCellProps) {
    renders.push(props.cell);
    return React.createElement(actual.CrosswordCellView, props);
  }
  CrosswordCellMock.displayName = 'CrosswordCellMock';
  return {
    ...actual,
    cellRenders: renders,
    CrosswordCell: React.memo(CrosswordCellMock),
  };
});

const fetchPuzzle = fetchJsonWithOfflineCache as jest.MockedFunction<typeof fetchJsonWithOfflineCache>;

beforeEach(() => {
  Object.assign(mockNetworkState, { isConnected: true, isInternetReachable: true });
  jest.clearAllMocks();
  cellRenders.length = 0;
  fetchPuzzle.mockResolvedValue(crosswordFixture());
});
afterEach(() => { jest.useRealTimers(); });

it('does not re-render cells when the play timer ticks', async () => {
  jest.useFakeTimers();
  const puzzle = crosswordFixture();
  renderWithProviders(
    <CrosswordSolver puzzle={puzzle} memberId={null} accessToken={null} onReload={jest.fn()} onNext={jest.fn()} />,
    { navigation: false },
  );
  await waitFor(() => expect(screen.getByRole('button', { name: 'B' })).toBeEnabled());
  const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
  await user.press(screen.getByRole('button', { name: 'B' }));
  expect(screen.getByTestId('crossword-timer')).toHaveTextContent('0:00');
  cellRenders.length = 0;
  await act(async () => { jest.advanceTimersByTime(1000); });
  expect(screen.getByTestId('crossword-timer')).toHaveTextContent('0:01');
  expect(cellRenders).toEqual([]);
  jest.useRealTimers();
});

it('keeps the screen timer test id after the play hook drops seconds', async () => {
  const navigation = fakeNavigation();
  renderWithProviders(<CrosswordPlayScreen navigation={navigation as never} route={{ name: 'CrosswordPlay', params: { slug: 'meet-the-band' } } as never} />, { navigation: false });
  await waitFor(() => expect(screen.getByTestId('crossword-timer')).toBeOnTheScreen());
});
