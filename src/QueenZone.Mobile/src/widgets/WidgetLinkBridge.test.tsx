import { act, waitFor } from '@testing-library/react-native';
import * as Linking from 'expo-linking';
import { WidgetLinkBridge } from './WidgetLinkBridge';
import { resetInitialWidgetUrlConsumption } from './widgetDeepLink';
import { renderWithProviders } from '../test/render';

const mockNavigate = jest.fn();

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return {
    ...actual,
    useNavigation: () => ({ navigate: mockNavigate }),
  };
});

jest.mock('expo-linking', () => ({
  getInitialURL: jest.fn(async () => null),
  addEventListener: jest.fn(() => ({ remove: jest.fn() })),
}));

const getInitialURL = Linking.getInitialURL as jest.MockedFunction<typeof Linking.getInitialURL>;
const addEventListener = Linking.addEventListener as jest.MockedFunction<typeof Linking.addEventListener>;

const homeDestination = {
  screen: 'HomeTab',
  params: { screen: 'Home', initial: false },
};

const quoteDestination = {
  screen: 'HomeTab',
  params: { screen: 'Quote', params: { id: 9 }, initial: false },
};

const timelineEventDestination = {
  screen: 'ArchiveTab',
  params: { screen: 'TimelineEvent', params: { id: 12 }, initial: false },
};

const timelineListDestination = {
  screen: 'ArchiveTab',
  params: { screen: 'Timeline', initial: false },
};

const triviaDestination = {
  screen: 'ArchiveTab',
  params: { screen: 'Trivia', params: { id: undefined, requestId: expect.any(Number) }, initial: false },
};

describe('WidgetLinkBridge', () => {
  beforeEach(() => {
    resetInitialWidgetUrlConsumption();
    mockNavigate.mockClear();
    getInitialURL.mockReset();
    addEventListener.mockReset();
    getInitialURL.mockResolvedValue(null);
    addEventListener.mockReturnValue({ remove: jest.fn() } as never);
  });

  it('opens the quote screen from a cold-start quote URL', async () => {
    getInitialURL.mockResolvedValue('queenzone://quotes/9');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', quoteDestination));
  });

  it('opens Home from a quote URL with a missing or non-integer id', async () => {
    getInitialURL.mockResolvedValue('queenzone://quotes/abc');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', homeDestination));
  });

  it('opens Home when launched from the widget URL', async () => {
    getInitialURL.mockResolvedValue('queenzone://home');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', homeDestination));
  });

  it('opens TimelineEvent from a cold-start day-face URL', async () => {
    getInitialURL.mockResolvedValue('queenzone://timeline/12');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', timelineEventDestination));
  });

  it('re-navigates TimelineEvent with a new id from a later day-face tap', async () => {
    let handler: ((event: { url: string }) => void) | undefined;
    addEventListener.mockImplementation((_type, next) => {
      handler = next as (event: { url: string }) => void;
      return { remove: jest.fn() } as never;
    });
    getInitialURL.mockResolvedValue('queenzone://timeline/12');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', timelineEventDestination));
    mockNavigate.mockClear();

    await act(async () => {
      handler?.({ url: 'queenzone://timeline/9999' });
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'ArchiveTab',
      params: { screen: 'TimelineEvent', params: { id: 9999 }, initial: false },
    });
  });

  it('opens the Timeline list from a day-face URL with no usable id', async () => {
    getInitialURL.mockResolvedValue('queenzone://timeline');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', timelineListDestination));
  });

  it('opens Trivia from a cold-start trivia URL', async () => {
    getInitialURL.mockResolvedValue('queenzone://trivia');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', triviaDestination));
  });

  it('opens Trivia from a later trivia url event', async () => {
    let handler: ((event: { url: string }) => void) | undefined;
    addEventListener.mockImplementation((_type, next) => {
      handler = next as (event: { url: string }) => void;
      return { remove: jest.fn() } as never;
    });

    await renderWithProviders(<WidgetLinkBridge />);
    await waitFor(() => expect(addEventListener).toHaveBeenCalled());

    await act(async () => {
      handler?.({ url: 'queenzone://trivia' });
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', triviaDestination);
  });

  it('opens Home from a later url event', async () => {
    let handler: ((event: { url: string }) => void) | undefined;
    addEventListener.mockImplementation((_type, next) => {
      handler = next as (event: { url: string }) => void;
      return { remove: jest.fn() } as never;
    });

    await renderWithProviders(<WidgetLinkBridge />);
    await waitFor(() => expect(addEventListener).toHaveBeenCalled());

    await act(async () => {
      handler?.({ url: 'queenzone://home' });
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', homeDestination);
  });

  it('does not re-apply the launch URL when the root navigator remounts', async () => {
    getInitialURL.mockResolvedValue('queenzone://home');
    const first = await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    await first.unmount();
    mockNavigate.mockClear();

    await renderWithProviders(<WidgetLinkBridge />);
    await waitFor(() => expect(getInitialURL).toHaveBeenCalledTimes(2));
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('still handles a later widget tap after the launch URL was consumed', async () => {
    let handler: ((event: { url: string }) => void) | undefined;
    addEventListener.mockImplementation((_type, next) => {
      handler = next as (event: { url: string }) => void;
      return { remove: jest.fn() } as never;
    });
    getInitialURL.mockResolvedValue('queenzone://home');
    await renderWithProviders(<WidgetLinkBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    mockNavigate.mockClear();

    await act(async () => {
      handler?.({ url: 'queenzone://home' });
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', homeDestination);
  });

  it('ignores unrelated deep links', async () => {
    getInitialURL.mockResolvedValue('queenzone://forum');
    await renderWithProviders(<WidgetLinkBridge />);
    await waitFor(() => expect(getInitialURL).toHaveBeenCalled());
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});

describe('trivia cold/warm link ordering', () => {
  beforeEach(() => { resetInitialWidgetUrlConsumption(); mockNavigate.mockClear(); getInitialURL.mockReset(); addEventListener.mockReset(); });
  it('routes a cold-start id and repeated/different warm taps', async () => {
    let handler: ((event: { url: string }) => void) | undefined;
    getInitialURL.mockResolvedValue('queenzone://trivia/12');
    addEventListener.mockImplementation((_type, next) => { handler = next as typeof handler; return { remove: jest.fn() } as never; });
    await renderWithProviders(<WidgetLinkBridge />);
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    await act(async () => { handler?.({ url: 'queenzone://trivia/13' }); handler?.({ url: 'queenzone://trivia/12' }); });
    const params = mockNavigate.mock.calls.map(call => call[1].params.params);
    expect(params.map(p => p.id)).toEqual([12, 13, 12]);
    expect(new Set(params.map(p => p.requestId)).size).toBe(3);
  });
  it('does not let a late initial URL replace a newer warm widget tap', async () => {
    let resolveInitial: (url: string) => void = () => {};
    let handler: ((event: { url: string }) => void) | undefined;
    getInitialURL.mockReturnValue(new Promise(resolve => { resolveInitial = resolve; }));
    addEventListener.mockImplementation((_type, next) => { handler = next as typeof handler; return { remove: jest.fn() } as never; });
    await renderWithProviders(<WidgetLinkBridge />);
    await act(async () => { handler?.({ url: 'queenzone://trivia/13' }); resolveInitial('queenzone://trivia/12'); });
    expect(mockNavigate).toHaveBeenCalledTimes(1);
    expect(mockNavigate.mock.calls[0][1].params.params.id).toBe(13);
  });
});
