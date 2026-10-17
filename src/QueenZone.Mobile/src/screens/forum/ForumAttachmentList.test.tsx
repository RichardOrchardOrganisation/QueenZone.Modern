import { screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { Alert } from 'react-native';
import {
  cacheForumAttachment,
  openForumAttachmentFile,
  openForumAttachmentImage,
  saveForumAttachmentImage,
} from '../../api';
import { ApiError } from '../../api/client';
import { SaveToPhotosError, saveToPhotosCopy } from '../../media/saveToPhotos';
import { forumAttachmentFixture } from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { ForumAttachmentList } from './ForumAttachmentList';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    openForumAttachmentFile: jest.fn(),
    openForumAttachmentImage: jest.fn(),
    cacheForumAttachment: jest.fn(),
    saveForumAttachmentImage: jest.fn(),
  };
});

jest.mock('../../config', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'http://qz.test', appEnv: 'production', version: '0.1.0' }),
}));

const openFile = openForumAttachmentFile as jest.MockedFunction<typeof openForumAttachmentFile>;
const openImage = openForumAttachmentImage as jest.MockedFunction<typeof openForumAttachmentImage>;
const cacheAttachment = cacheForumAttachment as jest.MockedFunction<typeof cacheForumAttachment>;
const saveImage = saveForumAttachmentImage as jest.MockedFunction<typeof saveForumAttachmentImage>;

const image = forumAttachmentFixture();
const pdf = forumAttachmentFixture({
  fileName: 'opera-side-two-notes.pdf',
  url: '/forum/attachment/legacy/1101',
  downloadUrl: '/api/v1/forum/attachments/legacy/1101',
  extension: 'PDF',
  formattedSize: '47.0 KB',
  isImage: false,
  thumbnailUrl: null,
});
const mp3 = forumAttachmentFixture({
  fileName: 'brighton-rock-solo.mp3',
  url: '/forum/attachment/legacy/1201',
  downloadUrl: '/api/v1/forum/attachments/legacy/1201',
  extension: 'MP3',
  formattedSize: '3.2 MB',
  isImage: false,
  thumbnailUrl: null,
});

async function renderList(attachments = [image]) {
  return await renderWithProviders(
    <ForumAttachmentList
      attachments={attachments}
      isSignedIn
      accessToken="tok"
      interactionsEnabled
    />,
  );
}

beforeEach(() => {
  openFile.mockReset();
  openFile.mockResolvedValue(undefined);
  openImage.mockReset();
  openImage.mockResolvedValue('data:image/jpeg;base64,dGVzdA==');
  cacheAttachment.mockReset();
  cacheAttachment.mockResolvedValue({
    fileUri: 'file:///cache/brighton-rock-solo.mp3',
    contentType: 'audio/mpeg',
    dataUri: 'data:audio/mpeg;base64,AA==',
  });
  saveImage.mockReset();
  saveImage.mockResolvedValue(undefined);
});

describe('ForumAttachmentList save', () => {
  it('keeps tap-to-view and saves the image to Photos', async () => {
    await renderList();
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadAttachmentViewer)).toBeOnTheScreen());
    expect(openImage).toHaveBeenCalledWith('/api/v1/forum/attachments/legacy/1002', 'tok');

    await user.press(screen.getByTestId(testIds.forumThreadAttachmentSave));
    await waitFor(() =>
      expect(saveImage).toHaveBeenCalledWith(
        '/api/v1/forum/attachments/legacy/1002',
        'tok',
        'anoto-setlist-scan.jpg',
      ),
    );
    expect(openFile).not.toHaveBeenCalled();
  });

  it('shows a Settings prompt message when Photos permission is denied', async () => {
    saveImage.mockRejectedValueOnce(
      new SaveToPhotosError('permission-denied', saveToPhotosCopy.denied),
    );
    jest.spyOn(Alert, 'alert');
    await renderList();
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadAttachmentSave)).toBeOnTheScreen());
    await user.press(screen.getByTestId(testIds.forumThreadAttachmentSave));
    await waitFor(() => expect(screen.getByText(saveToPhotosCopy.denied)).toBeOnTheScreen());
  });

  it('shares a non-image through the Files path', async () => {
    await renderList([pdf]);
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /opera-side-two-notes.pdf/ }));
    await waitFor(() =>
      expect(openFile).toHaveBeenCalledWith(
        '/api/v1/forum/attachments/legacy/1101',
        'tok',
        'opera-side-two-notes.pdf',
        { present: true },
      ),
    );
    expect(saveImage).not.toHaveBeenCalled();
  });

  it('plays audio from the cached file URI and still offers Files', async () => {
    await renderList([mp3]);
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /brighton-rock-solo.mp3/ }));
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadAttachmentAudio)).toBeOnTheScreen());
    expect(cacheAttachment).toHaveBeenCalledWith(
      '/api/v1/forum/attachments/legacy/1201',
      'tok',
      'brighton-rock-solo.mp3',
    );

    const player = screen.getByTestId(testIds.forumThreadAttachmentAudio);
    expect(within(player).getByTestId(testIds.forumThreadAttachmentAudioPlay)).toBeOnTheScreen();
    await user.press(screen.getByTestId(testIds.forumThreadAttachmentSaveFile));
    await waitFor(() =>
      expect(openFile).toHaveBeenCalledWith(
        '/api/v1/forum/attachments/legacy/1201',
        'tok',
        'brighton-rock-solo.mp3',
        { present: true },
      ),
    );
    expect(saveImage).not.toHaveBeenCalled();
  });

  it('shows a 401 on the existing caption and does not save', async () => {
    openImage.mockRejectedValueOnce(new ApiError(401, 'Sign in to continue.'));
    await renderList();
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() => expect(screen.getByText('Sign in to continue.')).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
    expect(saveImage).not.toHaveBeenCalled();
  });
});
