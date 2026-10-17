import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchRandomTrivia, fetchTriviaById, type RandomTrivia } from '../../api';
import { ApiError } from '../../api/client';
import { deferred } from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { TriviaScreen } from './TriviaScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchRandomTrivia: jest.fn(),
    fetchTriviaById: jest.fn(),
  };
});

const fetchTrivia = fetchRandomTrivia as jest.MockedFunction<typeof fetchRandomTrivia>;

const fetchById = fetchTriviaById as jest.MockedFunction<typeof fetchTriviaById>;

function triviaRoute(id?: number, requestId = 1) {
  return { key: "trivia", name: "Trivia" as const, params: { id, requestId } };
}

async function renderTrivia() {
  return await renderWithProviders(<TriviaScreen />, { navigation: false });
}

describe('widget fact selection', () => {
  beforeEach(() => { fetchTrivia.mockReset(); fetchById.mockReset(); });

  it('opens only the requested fact and lets Next fact resume random trivia', async () => {
    fetchById.mockResolvedValue({ id: 12, text: 'Widget fact.' });
    fetchTrivia.mockResolvedValue({ id: 99, text: 'Next random fact.' });
    const view = await renderWithProviders(<TriviaScreen route={triviaRoute(12)} />, { navigation: false });
    await waitFor(() => expect(screen.getByText('Widget fact.')).toBeOnTheScreen());
    expect(fetchById).toHaveBeenCalledWith(12, expect.any(AbortSignal));
    expect(fetchTrivia).not.toHaveBeenCalled();
    await userEvent.setup().press(screen.getByTestId(testIds.triviaNext));
    await waitFor(() => expect(screen.getByText('Next random fact.')).toBeOnTheScreen());
    await view.rerender(<TriviaScreen route={triviaRoute(12, 2)} />);
    await waitFor(() => expect(screen.getByText('Widget fact.')).toBeOnTheScreen());
    expect(fetchById).toHaveBeenCalledTimes(2);
  });

  it('changes a warm target and ignores an older request finishing later', async () => {
    const old = deferred<RandomTrivia>();
    fetchById.mockReturnValueOnce(old.promise).mockResolvedValueOnce({ id: 2, text: 'New widget fact.' });
    const view = await renderWithProviders(<TriviaScreen route={triviaRoute(1)} />, { navigation: false });
    await waitFor(() => expect(fetchById).toHaveBeenCalledTimes(1));
    const signal = fetchById.mock.calls[0][1];
    await view.rerender(<TriviaScreen route={triviaRoute(2, 2)} />);
    await waitFor(() => expect(screen.getByText('New widget fact.')).toBeOnTheScreen());
    expect(signal?.aborted).toBe(true);
    await act(async () => old.resolve({ id: 1, text: 'Old widget fact.' }));
    expect(screen.queryByText('Old widget fact.')).toBeNull();
    expect(screen.getByText('New widget fact.')).toBeOnTheScreen();
    expect(fetchTrivia).not.toHaveBeenCalled();
  });

  it('clears an existing warm fact immediately while the next target loads', async () => {
    const next = deferred<RandomTrivia>();
    fetchById.mockResolvedValueOnce({ id: 1, text: 'First widget fact.' }).mockReturnValueOnce(next.promise);
    const view = await renderWithProviders(<TriviaScreen route={triviaRoute(1)} />, { navigation: false });
    await waitFor(() => expect(screen.getByText('First widget fact.')).toBeOnTheScreen());
    await view.rerender(<TriviaScreen route={triviaRoute(2, 2)} />);
    expect(screen.queryByText('First widget fact.')).toBeNull();
    await act(async () => next.resolve({ id: 2, text: 'Second widget fact.' }));
    await waitFor(() => expect(screen.getByText('Second widget fact.')).toBeOnTheScreen());
  });

  it('does not substitute random trivia for missing or unpublished targets', async () => {
    fetchById.mockRejectedValue(new ApiError(404, 'This trivia fact is unavailable.'));
    await renderWithProviders(<TriviaScreen route={triviaRoute(12)} />, { navigation: false });
    await waitFor(() => expect(screen.getByText('This trivia fact is unavailable.')).toBeOnTheScreen());
    expect(fetchTrivia).not.toHaveBeenCalled();
    expect(screen.queryByTestId(testIds.triviaNext)).toBeNull();
  });

  it('rejects invalid route ids locally and preserves legacy random links', async () => {
    fetchTrivia.mockResolvedValue({ id: 3, text: 'Legacy random fact.' });
    const view = await renderWithProviders(<TriviaScreen route={triviaRoute(-1)} />, { navigation: false });
    await waitFor(() => expect(screen.getByText('This trivia fact is unavailable.')).toBeOnTheScreen());
    expect(fetchById).not.toHaveBeenCalled();
    await view.rerender(<TriviaScreen route={triviaRoute(undefined, 2)} />);
    await waitFor(() => expect(screen.getByText('Legacy random fact.')).toBeOnTheScreen());
  });
});

describe('TriviaScreen', () => {
  beforeEach(() => {
    fetchTrivia.mockReset();
    fetchById.mockReset();
  });

  it('shows a published fact, meta, and Next fact', async () => {
    fetchTrivia.mockResolvedValue({
      id: 12,
      text: 'The first Queen album was recorded in 1972.',
      category: 'Studio',
      difficulty: 'Easy',
      source: 'Queen archive',
    });
    await renderTrivia();

    await waitFor(() => expect(screen.getByTestId(testIds.triviaScreen)).toBeOnTheScreen());
    expect(screen.getByText('The first Queen album was recorded in 1972.')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.triviaMeta)).toHaveTextContent('Studio · Easy · Queen archive');
    expect(screen.getByTestId(testIds.triviaNext)).toBeOnTheScreen();
  });

  it('omits the meta row when category, difficulty, and source are blank', async () => {
    fetchTrivia.mockResolvedValue({
      id: 3,
      text: 'Brian May built the Red Special.',
      category: '   ',
      difficulty: null,
      source: undefined,
    });
    await renderTrivia();

    await waitFor(() => expect(screen.getByTestId(testIds.triviaScreen)).toBeOnTheScreen());
    expect(screen.getByText('Brian May built the Red Special.')).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.triviaMeta)).toBeNull();
  });

  it('fetches another fact when Next fact is pressed', async () => {
    const user = userEvent.setup();
    fetchTrivia
      .mockResolvedValueOnce({
        id: 1,
        text: 'First fact.',
      })
      .mockResolvedValueOnce({
        id: 2,
        text: 'Second fact.',
        category: 'Live',
      });
    await renderTrivia();

    await waitFor(() => expect(screen.getByText('First fact.')).toBeOnTheScreen());
    await user.press(screen.getByTestId(testIds.triviaNext));
    await waitFor(() => expect(screen.getByText('Second fact.')).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.triviaMeta)).toHaveTextContent('Live');
    expect(fetchTrivia).toHaveBeenCalledTimes(2);
  });

  it('shows an empty state when the API returns null', async () => {
    fetchTrivia.mockResolvedValue(null);
    await renderTrivia();

    await waitFor(() => expect(screen.getByTestId(testIds.triviaScreen)).toBeOnTheScreen());
    expect(screen.getByText('No trivia facts have been published yet.')).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.triviaNext)).toBeNull();
  });

  it('shows an error with retry when the request fails', async () => {
    const user = userEvent.setup();
    const pending = deferred<null>();
    fetchTrivia.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    fetchTrivia.mockReturnValueOnce(pending.promise);
    await renderTrivia();

    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    expect(screen.getByText('The server had a problem. Try again shortly.')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Try again' }));
    pending.resolve(null);
    await waitFor(() => expect(screen.getByTestId(testIds.triviaScreen)).toBeOnTheScreen());
    expect(screen.getByText('No trivia facts have been published yet.')).toBeOnTheScreen();
  });
});
