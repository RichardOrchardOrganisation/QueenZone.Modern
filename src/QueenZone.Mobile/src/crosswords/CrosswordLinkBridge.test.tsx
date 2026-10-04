import { act, render, waitFor } from '@testing-library/react-native';
import * as Linking from 'expo-linking';
import { CrosswordLinkBridge } from './CrosswordLinkBridge';
const mockNavigate = jest.fn();
jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ navigate: mockNavigate }) }));
jest.mock('expo-linking', () => ({ getInitialURL: jest.fn(), addEventListener: jest.fn() }));
it('routes cold and warm links and removes its listener when the navigator unmounts', async () => {
  const remove = jest.fn(); let handler!: (event: { url: string }) => void;
  (Linking.getInitialURL as jest.Mock).mockResolvedValue('queenzone://crosswords/meet-the-band');
  (Linking.addEventListener as jest.Mock).mockImplementation((_name, listener) => { handler = listener; return { remove }; });
  const view = render(<CrosswordLinkBridge />); await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
  act(() => handler({ url: 'https://www.queenzone.org/crosswords/live-aid' }));
  expect(mockNavigate).toHaveBeenLastCalledWith('Tabs', expect.objectContaining({ screen: 'ArchiveTab', params: expect.objectContaining({ params: { slug: 'live-aid' } }) }));
  act(() => handler({ url: 'queenzone://auth/callback' })); expect(mockNavigate).toHaveBeenCalledTimes(2);
  view.unmount(); expect(remove).toHaveBeenCalledTimes(1);
});
it('tolerates missing or failed initial URL lookup', async () => {
  mockNavigate.mockClear(); (Linking.getInitialURL as jest.Mock).mockRejectedValueOnce(new Error('Unavailable'));
  (Linking.addEventListener as jest.Mock).mockReturnValue({ remove: jest.fn() });
  const view = render(<CrosswordLinkBridge />); await act(async () => { await Promise.resolve(); });
  expect(mockNavigate).not.toHaveBeenCalled(); view.unmount();
});
