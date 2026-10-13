import { deferred } from '../../test/fixtures';
import type { TimelineEvent } from '../../api';
import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchTimelineEventById, fetchTimelinePage } from '../../api';
import { ApiError } from '../../api/client';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { TimelineEventScreen } from './TimelineEventScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchTimelineEventById: jest.fn(),
    fetchTimelinePage: jest.fn(),
  };
});

const fetchEvent = fetchTimelineEventById as jest.MockedFunction<typeof fetchTimelineEventById>;
const fetchList = fetchTimelinePage as jest.MockedFunction<typeof fetchTimelinePage>;

function renderEvent(navigation = fakeNavigation(), id = 9999) {
  return {
    navigation,
    ...renderWithProviders(
      <TimelineEventScreen
        navigation={navigation as never}
        route={{ key: 'timeline-event', name: 'TimelineEvent', params: { id } } as never}
      />,
      { navigation: false },
    ),
  };
}

describe('TimelineEventScreen', () => {
  it('keeps the newer warm event and its date when an older detail completes later', async () => {
    const old = deferred<TimelineEvent>();
    const fresh = { id: 2, title: 'New event', summary: 'New event summary', eventDate: '1985-07-13', formattedDate: '13 July 1985', category: 'live', categoryLabel: 'Live', sourceUrl: null };
    fetchEvent.mockReturnValueOnce(old.promise).mockResolvedValueOnce(fresh);
    const navigation = fakeNavigation();
    const view = renderEvent(navigation, 1);
    await waitFor(() => expect(fetchEvent).toHaveBeenCalledTimes(1));
    view.rerender(<TimelineEventScreen navigation={navigation as never} route={{ key: 'event', name: 'TimelineEvent', params: { id: 2 } }} />);
    await waitFor(() => expect(screen.getByText('New event')).toBeOnTheScreen());
    await act(async () => old.resolve({ ...fresh, id: 1, title: 'Old event', formattedDate: '01 July 1970' }));
    expect(screen.queryByText('Old event')).toBeNull();
    expect(screen.queryByText('01 July 1970')).toBeNull();
    expect(screen.getByText('13 July 1985')).toBeOnTheScreen();
    expect(fetchList).not.toHaveBeenCalled();
  });

  beforeEach(() => {
    fetchEvent.mockReset();
    fetchList.mockReset();
  });

  it('shows a deep off-page event from the by-id path, not the timeline list', async () => {
    fetchEvent.mockResolvedValue({
      id: 9999,
      title: 'Deep off-page event',
      summary: 'Would sit many pages down the chronological list.',
      eventDate: '1985-07-13T00:00:00Z',
      formattedDate: '13 July 1985',
      category: 'live',
      categoryLabel: 'Live',
      sourceUrl: 'https://en.wikipedia.org/wiki/Live_Aid',
    });
    renderEvent();

    await waitFor(() => expect(screen.getByTestId(testIds.timelineEventScreen)).toBeOnTheScreen());
    expect(fetchEvent).toHaveBeenCalledWith(9999, expect.any(AbortSignal));
    expect(fetchList).not.toHaveBeenCalled();
    expect(screen.getByText('Deep off-page event')).toBeOnTheScreen();
    expect(screen.getByText('Would sit many pages down the chronological list.')).toBeOnTheScreen();
    expect(screen.getByText('13 July 1985')).toBeOnTheScreen();
    expect(screen.getByText('Live')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.timelineEventSource)).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.timelineEventSeeAll)).toBeOnTheScreen();
  });

  it('navigates from the event into the Timeline list', async () => {
    fetchEvent.mockResolvedValue({
      id: 9999,
      title: 'Deep off-page event',
      summary: 'Would sit many pages down the chronological list.',
      eventDate: '1985-07-13T00:00:00Z',
      formattedDate: '13 July 1985',
      category: 'live',
      categoryLabel: 'Live',
      sourceUrl: null,
    });
    const navigation = fakeNavigation();
    renderEvent(navigation);

    await waitFor(() => expect(screen.getByTestId(testIds.timelineEventSeeAll)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.timelineEventSource)).toBeNull();
    await userEvent.press(screen.getByTestId(testIds.timelineEventSeeAll));
    expect(navigation.navigate).toHaveBeenCalledWith('Timeline');
  });

  it('replaces to the Timeline list when the event is unpublished or missing', async () => {
    fetchEvent.mockRejectedValue(new ApiError(404, 'Not Found'));
    const navigation = fakeNavigation();
    renderEvent(navigation);

    await waitFor(() => expect(navigation.replace).toHaveBeenCalledWith('Timeline'));
    expect(screen.queryByTestId(testIds.timelineEventScreen)).toBeNull();
    expect(fetchList).not.toHaveBeenCalled();
  });

  it('replaces to the Timeline list when the route id is not a positive integer', async () => {
    const navigation = fakeNavigation();
    renderEvent(navigation, 0);

    await waitFor(() => expect(navigation.replace).toHaveBeenCalledWith('Timeline'));
    expect(fetchEvent).not.toHaveBeenCalled();
    expect(fetchList).not.toHaveBeenCalled();
    expect(screen.queryByTestId(testIds.timelineEventScreen)).toBeNull();
  });
});
