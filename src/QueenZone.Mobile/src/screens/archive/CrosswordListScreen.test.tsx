import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchCrosswordsPage } from '../../api/crosswords';
import { pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { createMockSession } from '../../test/mockSession';
import { CrosswordListScreen } from './CrosswordListScreen';
const mockSession = createMockSession();
jest.mock('../../session/SessionContext', () => ({ useSession: () => mockSession }));
jest.mock('../../api/crosswords', () => ({ fetchCrosswordsPage: jest.fn() }));
const fetchPage = fetchCrosswordsPage as jest.MockedFunction<typeof fetchCrosswordsPage>;
const item = { id: '11111111-2222-4333-8444-555555555555', slug: 'meet-the-band', title: 'Meet the band',
  difficulty: 'easy' as const, width: 7, height: 7, publishedAt: '2026-10-04', progress: 'inProgress' as const, progressPercent: 40 };
function renderList() { const navigation = fakeNavigation(); return { navigation, ...renderWithProviders(<CrosswordListScreen navigation={navigation as never} route={{ name: 'CrosswordList' } as never} />, { navigation: false }) }; }
beforeEach(() => fetchPage.mockReset());
afterEach(flushVirtualizedList);
it('lists size, difficulty and personal status and opens a canonical slug', async () => {
  fetchPage.mockResolvedValue(pagedResponse([item])); const { navigation } = renderList();
  await waitFor(() => expect(screen.getByText('Meet the band')).toBeOnTheScreen());
  expect(screen.getByText('easy · 7×7 · In progress 40%')).toBeOnTheScreen();
  await userEvent.setup().press(screen.getByRole('button', { name: 'Open crossword Meet the band' }));
  expect(navigation.navigate).toHaveBeenCalledWith('CrosswordPlay', { slug: 'meet-the-band' });
});
it('reloads filters from page one instead of mixing differently filtered pages', async () => {
  fetchPage.mockResolvedValue(pagedResponse([item])); renderList();
  await waitFor(() => expect(screen.getByText('Meet the band')).toBeOnTheScreen());
  await userEvent.setup().press(screen.getByRole('button', { name: 'hard difficulties' }));
  await waitFor(() => expect(fetchPage).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, difficulty: 'hard' })));
  await userEvent.setup().press(screen.getByRole('button', { name: 'large grids' }));
  await waitFor(() => expect(fetchPage).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, difficulty: 'hard', size: 'large' })));
});
it('shows loading, empty and older API failure states without crashing', async () => {
  fetchPage.mockImplementation(() => new Promise(() => {})); const first = renderList();
  expect(screen.getByText('Loading crosswords…')).toBeOnTheScreen(); first.unmount();
  fetchPage.mockResolvedValue(pagedResponse([])); const second = renderList();
  await waitFor(() => expect(screen.getByText('No crosswords match these filters yet.')).toBeOnTheScreen()); second.unmount();
  fetchPage.mockRejectedValue(new Error('Crosswords are not available on this server yet.')); renderList();
  await waitFor(() => expect(screen.getByText('Something went wrong.')).toBeOnTheScreen());
});
