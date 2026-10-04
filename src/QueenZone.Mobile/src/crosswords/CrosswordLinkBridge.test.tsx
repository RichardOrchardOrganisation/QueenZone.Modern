import { act, render } from '@testing-library/react-native';
import { addEventListener } from 'expo-linking';
import { CrosswordLinkBridge } from './CrosswordLinkBridge';
const mockNavigate = jest.fn();
let mockUrl: string | null = null;
jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ navigate: mockNavigate }) }));
jest.mock('expo-linking', () => ({ useLinkingURL: () => mockUrl, addEventListener: jest.fn() }));
beforeEach(() => { mockNavigate.mockClear(); mockUrl = null; (addEventListener as jest.Mock).mockReturnValue({ remove: jest.fn() }); });
it('routes Expo scene-cached cold links and subsequent native URL events', () => {
  mockUrl = 'queenzone://crosswords/meet-the-band';
  const view = render(<CrosswordLinkBridge />);
  expect(mockNavigate).toHaveBeenCalledTimes(1);
  mockUrl = 'https://www.queenzone.org/crosswords/live-aid'; view.rerender(<CrosswordLinkBridge />);
  expect(mockNavigate).toHaveBeenLastCalledWith('Tabs', expect.objectContaining({ screen: 'ArchiveTab', params: expect.objectContaining({ params: { slug: 'live-aid' } }) }));
  mockUrl = 'queenzone://auth/callback'; view.rerender(<CrosswordLinkBridge />);
  expect(mockNavigate).toHaveBeenCalledTimes(2);
  view.unmount();
});
it('ignores absent URLs and unrelated website links', () => {
  const view = render(<CrosswordLinkBridge />);
  mockUrl = 'https://queenzone.com/crosswords/live-aid'; view.rerender(<CrosswordLinkBridge />);
  expect(mockNavigate).not.toHaveBeenCalled(); view.unmount();
});

it('reopens a repeated warm URL after leaving the puzzle, deduplicates hook/event delivery and cleans up', () => {
  const remove = jest.fn(); let listener!: (event: { url: string }) => void;
  (addEventListener as jest.Mock).mockImplementation((_type, handler) => { listener = handler; return { remove }; });
  const now = jest.spyOn(Date, 'now').mockReturnValue(1000);
  mockUrl = 'queenzone://crosswords/meet-the-band';
  const view = render(<CrosswordLinkBridge />);
  act(() => listener({ url: mockUrl! })); expect(mockNavigate).toHaveBeenCalledTimes(1);
  now.mockReturnValue(2000); act(() => listener({ url: mockUrl! }));
  expect(mockNavigate).toHaveBeenCalledTimes(2);
  view.unmount(); expect(remove).toHaveBeenCalledTimes(1); now.mockRestore();
});
