import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchFreddieTributePage, type FreddieTribute } from '../../api';
import { ApiError } from '../../api/client';
import { pagedResponse } from '../../test/fixtures';
import { flushVirtualizedList, renderWithProviders } from '../../test/render';
import { FreddieTributeScreen } from './FreddieTributeScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchFreddieTributePage: jest.fn(),
  };
});

const fetchPage = fetchFreddieTributePage as jest.MockedFunction<typeof fetchFreddieTributePage>;

function tributeFixture(overrides: Partial<FreddieTribute> = {}): FreddieTribute {
  return {
    id: 1,
    name: 'Jane',
    thought: 'A'.repeat(200),
    country: 'UK',
    dateText: '24 Nov 1991',
    timeText: null,
    ...overrides,
  };
}

describe('FreddieTributeScreen', () => {
  beforeEach(() => {
    fetchPage.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('lists tributes and expands a row without requiring a full remount', async () => {
    fetchPage.mockResolvedValue(
      pagedResponse([tributeFixture(), tributeFixture({ id: 2, name: 'Roger', thought: 'Short tribute.' })]),
    );
    await renderWithProviders(<FreddieTributeScreen />, { navigation: false });

    await waitFor(() => expect(screen.getByText('Jane')).toBeOnTheScreen());
    expect(screen.getByText('Roger')).toBeOnTheScreen();
    expect(screen.getByText('Read more')).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Tribute from Jane' }));
    expect(screen.queryByText('Read more')).toBeNull();
    expect(screen.getByText('A'.repeat(200))).toBeOnTheScreen();
  });

  it('shows an error and retries', async () => {
    fetchPage
      .mockRejectedValueOnce(new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'))
      .mockResolvedValueOnce(pagedResponse([tributeFixture()]));
    await renderWithProviders(<FreddieTributeScreen />, { navigation: false });
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.getByText('Jane')).toBeOnTheScreen());
  });
});
