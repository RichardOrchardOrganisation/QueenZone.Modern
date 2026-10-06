import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, renderHook, waitFor } from '@testing-library/react-native';
import { fetchCrosswordProgress, checkCrossword, completeCrossword } from '../api/crosswords';
import { enqueueCrosswordProgress } from '../offlineQueue';
import { isOfflineQueueOwnerCurrent } from '../offlineQueue/flusher';
import { crosswordFixture } from '../test/crosswordFixture';
import * as core from './core';
import { useCrosswordPlay } from './useCrosswordPlay';
const mockNetwork = { isConnected: true, isInternetReachable: true };
jest.mock('expo-network', () => ({ useNetworkState: () => mockNetwork }));
jest.mock('../offlineQueue', () => ({ enqueueCrosswordProgress: jest.fn().mockResolvedValue(undefined), useOfflineQueue: () => [] }));
jest.mock('../offlineQueue/flusher', () => ({ flushOfflineQueue: jest.fn().mockResolvedValue(undefined), isOfflineQueueOwnerCurrent: jest.fn().mockReturnValue(true) }));
jest.mock('../api/crosswords', () => ({ fetchCrosswordProgress: jest.fn(), checkCrossword: jest.fn(), completeCrossword: jest.fn(), revealCrossword: jest.fn() }));
const fetchProgress = fetchCrosswordProgress as jest.MockedFunction<typeof fetchCrosswordProgress>;
const check = checkCrossword as jest.MockedFunction<typeof checkCrossword>;
const complete = completeCrossword as jest.MockedFunction<typeof completeCrossword>;
const member = '33333333-4444-4555-8666-777777777777';
const puzzle = crosswordFixture();
const progress = { playVersion: puzzle.playVersion!, letters: 'B' + '.'.repeat(24), elapsedSeconds: 12, revealedCells: [], autoCheckUsed: false, updatedAt: '2026-10-04T06:00:00Z', startedAt: '2026-10-04T05:00:00Z' };
beforeEach(async () => { jest.clearAllMocks(); await AsyncStorage.clear(); mockNetwork.isConnected = true; mockNetwork.isInternetReachable = true;
  fetchProgress.mockResolvedValue(undefined); (isOfflineQueueOwnerCurrent as jest.Mock).mockReturnValue(true); });
it('keeps selectCell identity across ticks and state changes', async () => {
  const hook = renderHook(() => useCrosswordPlay(puzzle, null, null));
  await waitFor(() => expect(hook.result.current.ready).toBe(true));
  const first = hook.result.current.selectCell;
  act(() => { hook.result.current.selectCell(2); });
  expect(hook.result.current.state.cell).toBe(2);
  expect(hook.result.current.selectCell).toBe(first);
  act(() => hook.result.current.change(core.typeLetter(hook.result.current.model, hook.result.current.state, 'B')));
  expect(hook.result.current.selectCell).toBe(first);
  expect(hook.result.current).not.toHaveProperty('seconds');
  hook.unmount();
});
it('opening an empty puzzle never creates a blank write, and offline edits remain local before queued sync', async () => {
  const hook = renderHook(() => useCrosswordPlay(puzzle, member, 'token-a'));
  await waitFor(() => expect(hook.result.current.ready).toBe(true)); expect(enqueueCrosswordProgress).not.toHaveBeenCalled();
  mockNetwork.isConnected = false; hook.rerender(undefined);
  act(() => hook.result.current.change(core.typeLetter(hook.result.current.model, hook.result.current.state, 'B')));
  await waitFor(() => expect(AsyncStorage.setItem).toHaveBeenCalled());
  hook.unmount(); await waitFor(() => expect(enqueueCrosswordProgress).toHaveBeenCalledWith(expect.objectContaining({ memberId: member, progress: expect.objectContaining({ playVersion: puzzle.playVersion, letters: 'B' + '.'.repeat(24) }) })));
});
it('restores the newer remote whole grid, merges assistance and never queues for a signed-out owner', async () => {
  await AsyncStorage.setItem(core.progressStorageKey(puzzle.id, member), JSON.stringify({ ...progress, letters: 'R' + '.'.repeat(24), revealedCells: [1], autoCheckUsed: true }));
  fetchProgress.mockResolvedValue({ ...progress, updatedAt: '2026-10-04T07:00:00Z' });
  const hook = renderHook(() => useCrosswordPlay(puzzle, member, 'token-a')); await waitFor(() => expect(hook.result.current.ready).toBe(true));
  expect(hook.result.current.state.letters[0]).toBe('B'); expect(hook.result.current.state.revealedCells).toEqual([1]); expect(hook.result.current.state.autoCheckUsed).toBe(true);
  (isOfflineQueueOwnerCurrent as jest.Mock).mockReturnValue(false); (enqueueCrosswordProgress as jest.Mock).mockClear();
  act(() => hook.result.current.change(core.typeLetter(hook.result.current.model, hook.result.current.state, 'A')));
  hook.unmount(); await act(async () => { await Promise.resolve(); await Promise.resolve(); }); expect(enqueueCrosswordProgress).not.toHaveBeenCalled();
});
it('offers guest progress instead of silently uploading it into a new account', async () => {
  await AsyncStorage.setItem(core.progressStorageKey(puzzle.id), JSON.stringify(progress));
  const hook = renderHook(() => useCrosswordPlay(puzzle, member, 'token-a')); await waitFor(() => expect(hook.result.current.guest).not.toBeNull());
  expect(hook.result.current.state.letters).toBe('.'.repeat(25)); expect(enqueueCrosswordProgress).not.toHaveBeenCalled();
  act(() => hook.result.current.keepGuest()); await waitFor(() => expect(hook.result.current.state.letters[0]).toBe('B'));
  await waitFor(() => expect(enqueueCrosswordProgress).toHaveBeenCalled()); hook.unmount();
});
it('discards a check response for a grid edited while the request was in flight', async () => {
  let resolve!: (value: Awaited<ReturnType<typeof checkCrossword>>) => void;
  check.mockImplementation(() => new Promise(done => { resolve = done; }));
  const hook = renderHook(() => useCrosswordPlay(puzzle, null, null)); await waitFor(() => expect(hook.result.current.ready).toBe(true));
  let pending!: Promise<void>; act(() => { pending = hook.result.current.check('grid'); });
  act(() => hook.result.current.change(core.typeLetter(hook.result.current.model, hook.result.current.state, 'B')));
  await act(async () => { resolve({ playVersion: puzzle.playVersion!, cells: [{ index: 0, status: 'incorrect' }], complete: false, explanations: [] }); await pending; });
  expect(hook.result.current.state.incorrectCells).toEqual([]); hook.unmount();
});
it('retains the immutable member completion instead of replaying it', async () => {
  await AsyncStorage.setItem(core.progressStorageKey(puzzle.id, member), JSON.stringify({ ...progress, letters: 'A'.repeat(25) }));
  complete.mockResolvedValue({ playVersion: puzzle.playVersion!, correct: true, completion: { elapsedSeconds: 88, clean: true, rankingEligible: true, completedAt: '2026-10-04T08:00:00Z' }, review: [] });
  const hook = renderHook(() => useCrosswordPlay(puzzle, member, 'token-a')); await waitFor(() => expect(hook.result.current.completion?.elapsedSeconds).toBe(88));
  expect(complete).toHaveBeenCalledTimes(1); hook.unmount();
});

it.each(['wrong', 'failed'] as const)('does not automatically retry a %s completion until the solver explicitly retries', async outcome => {
  await AsyncStorage.setItem(core.progressStorageKey(puzzle.id, member), JSON.stringify({ ...progress, letters: 'A'.repeat(25) }));
  if (outcome === 'wrong') complete.mockResolvedValue({ playVersion: puzzle.playVersion!, correct: false, completion: null, review: [] });
  else complete.mockRejectedValue(new Error('Could not connect'));
  const hook = renderHook(() => useCrosswordPlay(puzzle, member, 'token-a'));
  await waitFor(() => expect(hook.result.current.status).toMatch(outcome === 'wrong' ? /Not quite/ : /Could not connect/));
  expect(complete).toHaveBeenCalledTimes(1); expect(hook.result.current.completion).toBeNull();
  act(() => hook.result.current.retryFinish()); await waitFor(() => expect(complete).toHaveBeenCalledTimes(2)); hook.unmount();
});
