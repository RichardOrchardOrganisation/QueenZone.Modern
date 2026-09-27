import { act, waitFor } from '@testing-library/react-native';
import * as Linking from 'expo-linking';
import { Platform } from 'react-native';
import { renderWithProviders } from '../test/render';
import { SiriLinkBridge } from './SiriLinkBridge';

const mockNavigate = jest.fn();
jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return { ...actual, useNavigation: () => ({ navigate: mockNavigate }) };
});
jest.mock('expo-linking', () => ({
  getInitialURL: jest.fn(async () => null),
  addEventListener: jest.fn(() => ({ remove: jest.fn() })),
}));

const getInitialURL = Linking.getInitialURL as jest.MockedFunction<typeof Linking.getInitialURL>;
const addEventListener = Linking.addEventListener as jest.MockedFunction<typeof Linking.addEventListener>;

describe('SiriLinkBridge', () => {
  const originalPlatform = Platform.OS;
  beforeEach(() => {
    Platform.OS = 'ios';
    mockNavigate.mockClear();
    getInitialURL.mockReset().mockResolvedValue(null);
    addEventListener.mockReset().mockReturnValue({ remove: jest.fn() } as never);
  });
  afterAll(() => { Platform.OS = originalPlatform; });

  it('opens News from a cold start', async () => {
    getInitialURL.mockResolvedValue('queenzone://news');
    renderWithProviders(<SiriLinkBridge />);
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab', params: { screen: 'NewsIndex', initial: false },
    }));
  });

  it('searches from a warm launch and ignores private destinations', () => {
    let handler: ((event: { url: string }) => void) | undefined;
    addEventListener.mockImplementation((_type, callback) => {
      handler = callback;
      return { remove: jest.fn() } as never;
    });
    renderWithProviders(<SiriLinkBridge />);
    act(() => handler?.({ url: 'queenzone://inbox' }));
    expect(mockNavigate).not.toHaveBeenCalled();
    act(() => handler?.({ url: 'queenzone://search?q=Queen%20II' }));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'ArchiveTab', params: { screen: 'Search', params: { query: 'Queen II' }, initial: false },
    });
    act(() => handler?.({ url: 'queenzone://album/12' }));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'ArchiveTab', params: { screen: 'Album', params: { id: 12 }, initial: false },
    });
    act(() => handler?.({ url: 'queenzone://timeline/42' }));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'ArchiveTab', params: { screen: 'Timeline', params: { focusId: 42 }, initial: false },
    });
    act(() => handler?.({ url: 'queenzone://trivia' }));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'ArchiveTab', params: { screen: 'Trivia', initial: false },
    });
  });
});
