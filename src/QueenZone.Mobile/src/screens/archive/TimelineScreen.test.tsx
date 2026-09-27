import type { ReactNode } from 'react';
import { FlatList } from 'react-native';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchTimelineAnchor, fetchTimelinePage } from '../../api';
import type { TimelineEvent } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { TimelineScreen } from './TimelineScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchTimelineAnchor: jest.fn(),
    fetchTimelinePage: jest.fn(),
  };
});

const fetchTimeline = fetchTimelinePage as jest.MockedFunction<typeof fetchTimelinePage>;
const fetchAnchor = fetchTimelineAnchor as jest.MockedFunction<typeof fetchTimelineAnchor>;

function eventFixture(overrides: Partial<TimelineEvent> = {}): TimelineEvent {
  return {
    id: 12,
    title: 'Live Aid',
    summary: 'Wembley Stadium.',
    eventDate: '1985-07-13T00:00:00Z',
    formattedDate: '13 July 1985',
    category: 'live',
    categoryLabel: 'Live',
    sourceUrl: null,
    ...overrides,
  };
}

describe('TimelineScreen', () => {
  beforeEach(() => {
    fetchTimeline.mockReset();
    fetchAnchor.mockReset();
    fetchAnchor.mockResolvedValue(pagedResponse([eventFixture()], 1, 1));
    fetchTimeline.mockResolvedValue(
      pagedResponse([eventFixture(), eventFixture({ id: 10, title: 'Another' })], 1, 1),
    );
    jest.spyOn(FlatList.prototype, 'scrollToIndex').mockImplementation(() => undefined);
  });

  afterEach(async () => {
    jest.restoreAllMocks();
    await flushVirtualizedList();
  });

  it('expands the event passed as focusId from search', async () => {
    renderWithProviders(
      <TimelineScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'timeline', name: 'Timeline', params: { focusId: 12 } } as never}
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Live Aid' }).props.accessibilityState).toEqual({
      expanded: true,
    });
    expect(screen.getByText('Wembley Stadium.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Another' }).props.accessibilityState).toEqual({
      expanded: false,
    });
    await waitFor(() =>
      expect(FlatList.prototype.scrollToIndex).toHaveBeenCalledWith(
        expect.objectContaining({ index: expect.any(Number) }),
      ),
    );
  });

  it('requests one anchor for a focused event on a later page', async () => {
    fetchTimeline.mockResolvedValue(pagedResponse([eventFixture({ id: 10, title: 'Another' })], 1, 20));
    fetchAnchor.mockResolvedValue(pagedResponse([eventFixture()], 20, 20));

    renderWithProviders(
      <TimelineScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'timeline', name: 'Timeline', params: { focusId: 12 } } as never}
      />,
      { navigation: false },
    );

    await waitFor(() => expect(fetchAnchor).toHaveBeenCalledTimes(1));
    expect(fetchAnchor).toHaveBeenCalledWith(12);
    expect(fetchTimeline).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Live Aid' }).props.accessibilityState).toEqual({
      expanded: true,
    });
    expect(screen.getByText('Wembley Stadium.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Another' }).props.accessibilityState).toEqual({
      expanded: false,
    });
    await waitFor(() =>
      expect(FlatList.prototype.scrollToIndex).toHaveBeenCalledWith(
        expect.objectContaining({ index: expect.any(Number) }),
      ),
    );
  });

  it('reapplies a new focusId when Timeline is already mounted', async () => {
    const navigation = fakeNavigation();
    const view = renderWithProviders(
      <TimelineScreen
        navigation={navigation as never}
        route={{ key: 'timeline', name: 'Timeline', params: { focusId: 12 } } as never}
      />,
      { navigation: false },
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Live Aid' }).props.accessibilityState).toEqual({
      expanded: true,
    });
    await waitFor(() =>
      expect(FlatList.prototype.scrollToIndex).toHaveBeenCalledWith(
        expect.objectContaining({ index: expect.any(Number) }),
      ),
    );
    (FlatList.prototype.scrollToIndex as jest.Mock).mockClear();

    view.rerender(
      <TimelineScreen
        navigation={navigation as never}
        route={{ key: 'timeline', name: 'Timeline', params: { focusId: 10 } } as never}
      />,
    );

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Another' }).props.accessibilityState).toEqual({
        expanded: true,
      }),
    );
    expect(screen.getByRole('button', { name: 'Live Aid' }).props.accessibilityState).toEqual({
      expanded: false,
    });
    await waitFor(() =>
      expect(FlatList.prototype.scrollToIndex).toHaveBeenCalledWith(
        expect.objectContaining({ index: expect.any(Number) }),
      ),
    );
  });

  it('includes the anchor event when focusId is missing from the loaded page', async () => {
    fetchTimeline.mockResolvedValue(pagedResponse([eventFixture({ id: 10, title: 'Another' })], 1, 1));
    fetchAnchor.mockResolvedValue(pagedResponse([eventFixture()], 2, 2));

    renderWithProviders(
      <TimelineScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'timeline', name: 'Timeline', params: { focusId: 12 } } as never}
      />,
      { navigation: false },
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Live Aid' }).props.accessibilityState).toEqual({
      expanded: true,
    });
    expect(screen.getByText('Wembley Stadium.')).toBeOnTheScreen();
    await waitFor(() =>
      expect(FlatList.prototype.scrollToIndex).toHaveBeenCalledWith(
        expect.objectContaining({ index: expect.any(Number) }),
      ),
    );
  });

  it('leaves the list unexpanded when the focused event is never found', async () => {
    fetchTimeline.mockResolvedValue(pagedResponse([eventFixture({ id: 10, title: 'Another' })], 1, 1));
    fetchAnchor.mockRejectedValue(new Error('Missing event'));

    renderWithProviders(
      <TimelineScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'timeline', name: 'Timeline', params: { focusId: 99 } } as never}
      />,
      { navigation: false },
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'Another' })).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Another' }).props.accessibilityState).toEqual({
      expanded: false,
    });
    expect(screen.queryByText('Wembley Stadium.')).toBeNull();
    expect(FlatList.prototype.scrollToIndex).not.toHaveBeenCalled();
  });

  it('pops back to the archive listing when the stack has history', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(true);
    renderTimeline(navigation);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());

    const user = userEvent.setup();
    renderWithProviders(<>{lastHeaderLeft(navigation)()}</>, { navigation: false });
    await user.press(screen.getByTestId(testIds.timelineBack));
    expect(navigation.goBack).toHaveBeenCalledTimes(1);
    expect(navigation.navigate).not.toHaveBeenCalled();
  });

  it('falls back to ArchiveHub when Timeline is the only route', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(false);
    renderTimeline(navigation);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());

    const user = userEvent.setup();
    renderWithProviders(<>{lastHeaderLeft(navigation)()}</>, { navigation: false });
    await user.press(screen.getByTestId(testIds.timelineBack));
    expect(navigation.goBack).not.toHaveBeenCalled();
    expect(navigation.navigate).toHaveBeenCalledWith('ArchiveHub');
  });
});

function lastHeaderLeft(navigation: ReturnType<typeof fakeNavigation>) {
  const calls = navigation.setOptions.mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  const options = calls[calls.length - 1]?.[0] as { headerLeft?: () => ReactNode };
  expect(options.headerLeft).toEqual(expect.any(Function));
  return options.headerLeft!;
}

function renderTimeline(navigation = fakeNavigation()) {
  return renderWithProviders(
    <TimelineScreen
      navigation={navigation as never}
      route={{ key: 'timeline', name: 'Timeline', params: { focusId: 12 } } as never}
    />,
    { navigation: false },
  );
}
