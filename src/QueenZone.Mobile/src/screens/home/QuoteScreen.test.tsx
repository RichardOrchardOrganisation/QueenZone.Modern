import { deferred } from '../../test/fixtures';
import type { RandomQuote } from '../../api';
import { act, screen, waitFor } from '@testing-library/react-native';
import { fetchQuoteById } from '../../api';
import { ApiError } from '../../api/client';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { QuoteScreen } from './QuoteScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchQuoteById: jest.fn(),
  };
});

const fetchQuote = fetchQuoteById as jest.MockedFunction<typeof fetchQuoteById>;

function renderQuote(navigation = fakeNavigation(), id = 9) {
  return {
    navigation,
    ...renderWithProviders(
      <QuoteScreen
        navigation={navigation as never}
        route={{ key: 'quote', name: 'Quote', params: { id } } as never}
      />,
      { navigation: false },
    ),
  };
}

describe('QuoteScreen', () => {
  beforeEach(() => {
    fetchQuote.mockReset();
  });

  it('keeps the newer warm quote when an older detail request finishes later', async () => {
    const old = deferred<RandomQuote>();
    fetchQuote.mockReturnValueOnce(old.promise).mockResolvedValueOnce({ id: 10, text: 'New quote', whoSaid: 'Brian' });
    const navigation = fakeNavigation();
    const view = renderQuote(navigation, 9);
    await waitFor(() => expect(fetchQuote).toHaveBeenCalledTimes(1));
    view.rerender(<QuoteScreen navigation={navigation as never} route={{ key: 'quote', name: 'Quote', params: { id: 10 } }} />);
    await waitFor(() => expect(screen.getByText('“New quote”')).toBeOnTheScreen());
    await act(async () => old.resolve({ id: 9, text: 'Old quote', whoSaid: 'Freddie' }));
    expect(screen.queryByText('“Old quote”')).toBeNull();
    expect(screen.getByText('“New quote”')).toBeOnTheScreen();
  });


  it('shows the published quote and Context when present', async () => {
    fetchQuote.mockResolvedValue({
      id: 9,
      text: 'A kind of magic',
      whoSaid: 'Freddie Mercury',
      context: 'Live Aid, 1985',
    });
    renderQuote();

    await waitFor(() => expect(screen.getByTestId(testIds.quoteScreen)).toBeOnTheScreen());
    expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen();
    expect(screen.getByText('— Freddie Mercury')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.quoteContext)).toBeOnTheScreen();
    expect(screen.getByText('Context')).toBeOnTheScreen();
    expect(screen.getByText('Live Aid, 1985')).toBeOnTheScreen();
  });

  it('omits the Context block when the quote has no context', async () => {
    fetchQuote.mockResolvedValue({
      id: 9,
      text: 'A kind of magic',
      whoSaid: 'Freddie Mercury',
      context: '   ',
    });
    renderQuote();

    await waitFor(() => expect(screen.getByTestId(testIds.quoteScreen)).toBeOnTheScreen());
    expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.quoteContext)).toBeNull();
    expect(screen.queryByText('Context')).toBeNull();
  });

  it('replaces to Home when the quote is unpublished or missing', async () => {
    fetchQuote.mockRejectedValue(new ApiError(404, 'Not Found'));
    const navigation = fakeNavigation();
    renderQuote(navigation);

    await waitFor(() => expect(navigation.replace).toHaveBeenCalledWith('Home'));
    expect(screen.queryByTestId(testIds.quoteScreen)).toBeNull();
  });

  it('replaces to Home when the route id is not a positive integer', async () => {
    const navigation = fakeNavigation();
    renderQuote(navigation, 0);

    await waitFor(() => expect(navigation.replace).toHaveBeenCalledWith('Home'));
    expect(fetchQuote).not.toHaveBeenCalled();
    expect(screen.queryByTestId(testIds.quoteScreen)).toBeNull();
  });
});
