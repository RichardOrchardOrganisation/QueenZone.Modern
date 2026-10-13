import { act, waitFor } from '@testing-library/react-native';
import * as Linking from 'expo-linking';
import { Platform } from 'react-native';
import { SiriLinkBridge } from '../siri/SiriLinkBridge';
import { renderWithProviders } from '../test/render';
import { WidgetLinkBridge } from './WidgetLinkBridge';
import { resetInitialWidgetUrlConsumption } from './widgetDeepLink';

const mockNavigate = jest.fn();
const mockHandlers: ((event: { url: string }) => void)[] = [];

jest.mock('@react-navigation/native', () => ({
  ...jest.requireActual('@react-navigation/native'),
  useNavigation: () => ({ navigate: mockNavigate }),
}));
jest.mock('expo-linking', () => ({
  getInitialURL: jest.fn(async () => null),
  addEventListener: jest.fn((_kind, handler) => {
    mockHandlers.push(handler);
    return { remove: jest.fn() };
  }),
}));

const getInitialURL = Linking.getInitialURL as jest.MockedFunction<typeof Linking.getInitialURL>;
const destinations = [
  ['queenzone://timeline/5', { screen: 'ArchiveTab', params: { screen: 'TimelineEvent', params: { id: 5 }, initial: false } }],
  ['queenzone://timeline', { screen: 'ArchiveTab', params: { screen: 'Timeline', initial: false } }],
  ['queenzone://quotes/2', { screen: 'HomeTab', params: { screen: 'Quote', params: { id: 2 }, initial: false } }],
  ['queenzone://trivia/1', { screen: 'ArchiveTab', params: { screen: 'Trivia', params: { id: 1, requestId: expect.any(Number) }, initial: false } }],
  ['queenzone://trivia', { screen: 'ArchiveTab', params: { screen: 'Trivia', params: { id: undefined, requestId: expect.any(Number) }, initial: false } }],
] as const;

for (const platform of ['ios', 'android'] as const) {
  describe(`${platform} joint link ownership`, () => {
    const originalPlatform = Platform.OS;
    beforeEach(() => {
      Platform.OS = platform;
      resetInitialWidgetUrlConsumption();
      mockNavigate.mockClear();
      mockHandlers.length = 0;
      getInitialURL.mockReset().mockResolvedValue(null);
    });
    afterAll(() => { Platform.OS = originalPlatform; });

    it.each(destinations)('opens one cold destination for %s', async (url, destination) => {
      getInitialURL.mockResolvedValue(url);
      renderWithProviders(<><WidgetLinkBridge /><SiriLinkBridge /></>);
      await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
      expect(mockNavigate).toHaveBeenCalledWith('Tabs', destination);
    });

    it.each(destinations)('opens one destination for each repeated warm %s tap', async (url, destination) => {
      renderWithProviders(<><WidgetLinkBridge /><SiriLinkBridge /></>);
      await waitFor(() => expect(mockHandlers).toHaveLength(platform === 'ios' ? 2 : 1));
      await act(async () => {
        for (let tap = 0; tap < 2; tap++) mockHandlers.forEach(handler => handler({ url }));
      });
      expect(mockNavigate).toHaveBeenCalledTimes(2);
      expect(mockNavigate.mock.calls).toEqual([['Tabs', destination], ['Tabs', destination]]);
    });

    it('keeps the latest warm event when a cold URL resolves later', async () => {
      let resolveInitial: (url: string) => void = () => {};
      getInitialURL.mockReturnValue(new Promise(resolve => { resolveInitial = resolve; }));
      renderWithProviders(<><WidgetLinkBridge /><SiriLinkBridge /></>);
      await act(async () => {
        mockHandlers.forEach(handler => handler({ url: 'queenzone://timeline/6' }));
        resolveInitial('queenzone://timeline/5');
      });
      expect(mockNavigate).toHaveBeenCalledTimes(1);
      expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
        screen: 'ArchiveTab', params: { screen: 'TimelineEvent', params: { id: 6 }, initial: false },
      });
    });
  });
}
