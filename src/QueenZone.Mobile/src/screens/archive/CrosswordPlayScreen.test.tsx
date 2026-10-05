import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';
import { fetchJsonWithOfflineCache } from '../../cache';
import { checkCrossword, revealCrossword } from '../../api/crosswords';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { createMockSession } from '../../test/mockSession';
import { progressStorageKey } from '../../crosswords/core';
import { crosswordFixture } from '../../test/crosswordFixture';
import { CrosswordPlayScreen } from './CrosswordPlayScreen';
const mockSession = createMockSession();
jest.mock('../../session/SessionContext', () => ({ useSession: () => mockSession }));
jest.mock('../../cache', () => ({ fetchJsonWithOfflineCache: jest.fn() }));
const mockNetworkState = { isConnected: true, isInternetReachable: true };
jest.mock('expo-network', () => ({ useNetworkState: () => mockNetworkState }));
jest.mock('expo-haptics', () => ({ selectionAsync: jest.fn().mockResolvedValue(undefined) }));
jest.mock('../../api/crosswords', () => ({ checkCrossword: jest.fn(), revealCrossword: jest.fn(), completeCrossword: jest.fn(), fetchCrosswordProgress: jest.fn().mockResolvedValue(undefined) }));
const fetchPuzzle = fetchJsonWithOfflineCache as jest.MockedFunction<typeof fetchJsonWithOfflineCache>;
const check = checkCrossword as jest.MockedFunction<typeof checkCrossword>;
const reveal = revealCrossword as jest.MockedFunction<typeof revealCrossword>;
function renderPlay() { const navigation = fakeNavigation(); return { navigation, ...renderWithProviders(<CrosswordPlayScreen navigation={navigation as never} route={{ name: 'CrosswordPlay', params: { slug: 'meet-the-band' } } as never} />, { navigation: false }) }; }
beforeEach(async () => { Object.assign(mockNetworkState, { isConnected: true, isInternetReachable: true }); jest.clearAllMocks(); await AsyncStorage.clear(); fetchPuzzle.mockResolvedValue(crosswordFixture()); });
it('loads the cached public shape and renders errors or absent older API gracefully', async () => {
  fetchPuzzle.mockImplementation(() => new Promise(() => {})); const first = renderPlay();
  expect(screen.getByText('Loading crossword…')).toBeOnTheScreen(); first.unmount();
  fetchPuzzle.mockRejectedValue(new Error('Crosswords are unavailable.')); const second = renderPlay();
  await waitFor(() => expect(screen.getByText('Something went wrong.')).toBeOnTheScreen()); second.unmount();
  fetchPuzzle.mockResolvedValue(crosswordFixture({ playVersion: undefined, archived: true })); renderPlay();
  await waitFor(() => expect(screen.getByText('Archived crossword · still playable')).toBeOnTheScreen());
  await waitFor(() => expect(screen.getByText('This server does not support crossword saves yet. You can explore the blank grid.')).toBeOnTheScreen());
  expect(screen.getByRole('button', { name: 'Check' })).toBeDisabled();
});
it('types, backspaces, switches clues and checks the current word with a required version', async () => {
  const puzzle = crosswordFixture(); check.mockResolvedValue({ playVersion: puzzle.playVersion!, cells: [{ index: 0, status: 'incorrect' }], explanations: [], complete: false });
  renderPlay(); await waitFor(() => expect(screen.getByRole('button', { name: 'B' })).toBeEnabled());
  expect(fetchPuzzle).toHaveBeenCalledWith('/crosswords/by-slug/meet-the-band', expect.objectContaining({ cacheKey: 'crosswords:detail:meet-the-band', invalidateOn: [404] }));
  const user = userEvent.setup(); await user.press(screen.getByRole('button', { name: 'B' }));
  await user.press(screen.getByRole('button', { name: 'R' }));
  await user.press(screen.getByRole('button', { name: 'Backspace' }));
  await user.press(screen.getByRole('button', { name: 'Check' })); await user.press(screen.getByRole('button', { name: 'Check word' }));
  await waitFor(() => expect(check).toHaveBeenCalledWith(puzzle.id, puzzle.playVersion, 'B' + '.'.repeat(24), { scope: 'entry', number: 1, direction: 'across' }, false, expect.any(Object)));
  expect(screen.getByTestId('crossword-status')).toHaveTextContent(/Some letters are incorrect/);
  await user.press(screen.getByRole('button', { name: 'Next clue' }));
  expect(screen.getByTestId('crossword-active-clue')).toHaveTextContent(/6 across/);
  expect(screen.queryByRole('textbox')).toBeNull();
});
it('requires reveal confirmation and keeps assistance across local reload', async () => {
  const puzzle = crosswordFixture(); reveal.mockResolvedValue({ playVersion: puzzle.playVersion!, cells: [{ index: 0, letter: 'B' }], explanations: [], clean: false });
  const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
  const first = renderPlay(); await waitFor(() => expect(screen.getByRole('button', { name: 'B' })).toBeEnabled());
  const user = userEvent.setup(); await user.press(screen.getByRole('button', { name: 'Reveal' })); await user.press(screen.getByRole('button', { name: 'Reveal word' }));
  expect(reveal).not.toHaveBeenCalled(); const buttons = alert.mock.calls[0][2]!;
  act(() => buttons.find(button => button.text === 'Reveal')?.onPress?.());
  await waitFor(() => expect(screen.getByTestId('crossword-status')).toHaveTextContent(/assisted solve/));
  await waitFor(() => expect(AsyncStorage.setItem).toHaveBeenCalled()); first.unmount();
  renderPlay(); await waitFor(() => expect(screen.getByTestId('crossword-cell-0')).toBeEnabled());
  expect(screen.getByTestId('crossword-cell-0').props.accessibilityLabel).toMatch(/revealed/i); alert.mockRestore();
});
it('hides the grid while manually paused and does not overwrite a pending restore on unmount', async () => {
  const first = renderPlay(); await waitFor(() => expect(screen.getByRole('button', { name: 'Pause' })).toBeEnabled());
  await userEvent.setup().press(screen.getByRole('button', { name: 'Pause' }));
  expect(screen.getByText('Paused')).toBeOnTheScreen(); expect(screen.getByRole('button', { name: 'A' })).toBeDisabled(); first.unmount();
  await AsyncStorage.clear(); (AsyncStorage.setItem as jest.Mock).mockClear();
  const original = (AsyncStorage.getItem as jest.Mock).getMockImplementation(); let release!: (value: string | null) => void;
  (AsyncStorage.getItem as jest.Mock).mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const second = renderPlay(); await waitFor(() => expect(screen.getByTestId('crossword-play-screen')).toBeOnTheScreen()); second.unmount();
  await act(async () => { release(null); await Promise.resolve(); });
  expect(AsyncStorage.setItem).not.toHaveBeenCalled(); (AsyncStorage.getItem as jest.Mock).mockImplementation(original);
});

it('offers review explanations after a guest completes the restored grid', async () => {
  const puzzle = crosswordFixture();
  await AsyncStorage.setItem(progressStorageKey(puzzle.id), JSON.stringify({
    playVersion: puzzle.playVersion, letters: 'A'.repeat(25), elapsedSeconds: 12,
    revealedCells: [], autoCheckUsed: false, updatedAt: '2026-10-04T06:00:00Z', startedAt: '2026-10-04T05:00:00Z',
  }));
  check.mockResolvedValue({ playVersion: puzzle.playVersion!, cells: [], complete: true,
    explanations: [{ number: 1, direction: 'across', explanation: 'Brian May plays guitar.' }],
  });
  renderPlay();
  await waitFor(() => expect(screen.getByText('Crossword complete')).toBeOnTheScreen());
  expect(screen.getByText(/Guest progress is saved/)).toBeOnTheScreen();
  expect(screen.queryByRole('button', { name: 'A' })).toBeNull();
  await userEvent.setup().press(screen.getByRole('button', { name: 'Review clues' }));
  expect(screen.getByText('Brian May plays guitar.')).toBeOnTheScreen();
});

it('checks the grid from the clues sheet and opens the returned explanations', async () => {
  const puzzle = crosswordFixture();
  check.mockResolvedValue({ playVersion: puzzle.playVersion!, cells: [], complete: false,
    explanations: [{ number: 1, direction: 'across', explanation: 'Queen guitarist explained.' }],
  });
  renderPlay();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Clues' })).toBeEnabled());
  const user = userEvent.setup();
  await user.press(screen.getByRole('button', { name: 'Clues' }));
  await user.press(screen.getByRole('button', { name: 'Check grid' }));
  await waitFor(() => expect(check).toHaveBeenCalledWith(puzzle.id, puzzle.playVersion,
    '.'.repeat(25), { scope: 'grid' }, false, expect.any(Object)));
  await user.press(screen.getByRole('button', { name: 'Clues' }));
  await user.press(screen.getByRole('button', { name: 'Review explanations' }));
  expect(screen.getByText('Queen guitarist explained.')).toBeOnTheScreen();
  expect(screen.queryByRole('button', { name: 'Check grid' })).toBeNull();
});

it('explains offline play while keeping local letter entry available', async () => {
  mockNetworkState.isConnected = false;
  renderPlay();
  await waitFor(() => expect(screen.getByRole('button', { name: 'B' })).toBeEnabled());
  expect(screen.getByText('Offline · letters saved on this device; checks need a connection')).toBeOnTheScreen();
  expect(screen.getByRole('button', { name: 'Check' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Reveal' })).toBeDisabled();
  await userEvent.setup().press(screen.getByRole('button', { name: 'B' }));
  expect(check).not.toHaveBeenCalled();
});
