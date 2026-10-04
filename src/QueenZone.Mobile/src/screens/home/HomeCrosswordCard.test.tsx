import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchCrosswordsPage } from '../../api/crosswords';
import { pagedResponse } from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { createMockSession } from '../../test/mockSession';
import { HomeCrosswordCard } from './HomeCrosswordCard';
const mockSession = createMockSession();
jest.mock('../../session/SessionContext', () => ({ useSession: () => mockSession }));
jest.mock('../../api/crosswords', () => ({ fetchCrosswordsPage: jest.fn() }));
const fetchPage = fetchCrosswordsPage as jest.MockedFunction<typeof fetchCrosswordsPage>;
const item = { id: '11111111-2222-4333-8444-555555555555', slug: 'meet-the-band', title: 'Meet the band', difficulty: 'easy' as const, width: 7, height: 7, publishedAt: '2026-10-04' };
beforeEach(() => fetchPage.mockReset());
it.each(['notStarted', 'inProgress', 'completed'] as const)('offers the correct action for %s and opens the newest slug', async progress => {
  fetchPage.mockResolvedValue(pagedResponse([{ ...item, progress }])); const play = jest.fn();
  renderWithProviders(<HomeCrosswordCard onPlay={play} />);
  await waitFor(() => expect(screen.getByText('Meet the band')).toBeOnTheScreen());
  const label = progress === 'inProgress' ? 'Continue crossword' : 'Play crossword';
  await userEvent.setup().press(screen.getByRole('button', { name: label })); expect(play).toHaveBeenCalledWith('meet-the-band');
});
it('hides when no published puzzles exist or the older API has no feature', async () => {
  fetchPage.mockResolvedValue(pagedResponse([])); const first = renderWithProviders(<HomeCrosswordCard onPlay={jest.fn()} />);
  await waitFor(() => expect(fetchPage).toHaveBeenCalled()); expect(screen.queryByTestId('home-crossword-card')).toBeNull(); first.unmount();
  fetchPage.mockRejectedValue(new Error('404')); renderWithProviders(<HomeCrosswordCard onPlay={jest.fn()} />);
  await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(2)); expect(screen.queryByTestId('home-crossword-card')).toBeNull();
});
