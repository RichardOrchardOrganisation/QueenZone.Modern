import { screen } from '@testing-library/react-native';
import { renderWithProviders } from '../../../test/render';
import { ForumVideoBody } from './ForumVideoBody';
import { testIds } from '../../../test/testIds';
const descriptor = { provider: 'youtube', videoId: 'M7lc1UVf-VE', startSeconds: null, watchUrl: 'https://www.youtube.com/watch?v=M7lc1UVf-VE', anchorIndex: 1 };
describe('forum positional native body', () => {
  it('adds a card at the mapped eligible link while keeping prose, unrelated and duplicate links', () => {
    const html = '<p>Before <a href="https://qz.test/">Source</a></p><p><strong><a href="https://youtu.be/M7lc1UVf-VE">Original</a></strong></p><p><a href="https://youtu.be/M7lc1UVf-VE">Duplicate</a></p><p>After</p>';
    renderWithProviders(<ForumVideoBody html={html} descriptors={[descriptor]} postKey="1" />, { navigation: false });
    expect(screen.getByText(/Before/)).toBeOnTheScreen(); expect(screen.getByText('Source')).toBeOnTheScreen();
    expect(screen.getByText('Original')).toBeOnTheScreen(); expect(screen.getByText('Duplicate')).toBeOnTheScreen(); expect(screen.getByText('After')).toBeOnTheScreen();
    expect(screen.getAllByTestId(testIds.forumYoutubeWatch)).toHaveLength(1);
    expect(screen.queryByTestId(testIds.forumYoutubePlayer)).toBeNull();
  });
  it.each([undefined, [], [{ ...descriptor, videoId: 'bad' }]])('renders readable old-server and malformed metadata bodies (%s)', descriptors => {
    renderWithProviders(<ForumVideoBody html='<p>Text <a href="https://youtu.be/M7lc1UVf-VE">Ordinary link</a></p>' descriptors={descriptors} postKey="2" />, { navigation: false });
    expect(screen.getByText('Ordinary link')).toBeOnTheScreen(); expect(screen.queryByTestId(testIds.forumYoutubeWatch)).toBeNull();
  });
});
