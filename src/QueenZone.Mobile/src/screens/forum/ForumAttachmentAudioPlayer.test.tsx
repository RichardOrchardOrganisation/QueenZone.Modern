import { screen, userEvent } from '@testing-library/react-native';
import { useAudioPlayer } from 'expo-audio';
import { renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { ForumAttachmentAudioPlayer } from './ForumAttachmentAudioPlayer';

const mockPlayer = {
  play: jest.fn(),
  pause: jest.fn(),
};

const mockStatus = {
  playing: false,
  isLoaded: true,
  currentTime: 0,
  duration: 12,
  didJustFinish: false,
  isBuffering: false,
  playbackState: 'ready',
  reasonForWaitingToPlay: '',
};

jest.mock('expo-audio', () => ({
  useAudioPlayer: jest.fn(() => mockPlayer),
  useAudioPlayerStatus: () => ({ ...mockStatus }),
}));

describe('ForumAttachmentAudioPlayer', () => {
  beforeEach(() => {
    mockPlayer.play.mockReset();
    mockPlayer.pause.mockReset();
    mockStatus.playing = false;
    mockStatus.playbackState = 'ready';
    mockStatus.reasonForWaitingToPlay = '';
  });

  it('plays from the file URI without FanPerformance options', async () => {
    const onSave = jest.fn();
    await renderWithProviders(
      <ForumAttachmentAudioPlayer
        fileUri="file:///cache/brighton-rock-solo.mp3"
        fileName="brighton-rock-solo.mp3"
        onSaveToFiles={onSave}
        saveBusy={false}
        saveError={null}
      />,
    );

    expect(useAudioPlayer).toHaveBeenCalledWith({ uri: 'file:///cache/brighton-rock-solo.mp3' });
    expect((useAudioPlayer as jest.Mock).mock.calls[0]?.[1]).toBeUndefined();

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.forumThreadAttachmentAudioPlay));
    expect(mockPlayer.play).toHaveBeenCalled();

    await user.press(screen.getByTestId(testIds.forumThreadAttachmentSaveFile));
    expect(onSave).toHaveBeenCalled();
  });

  it('keeps Files available after a decode failure', async () => {
    mockPlayer.play.mockImplementation(() => {
      throw new Error('decode failed');
    });
    const onSave = jest.fn();
    await renderWithProviders(
      <ForumAttachmentAudioPlayer
        fileUri="file:///cache/broken.mp3"
        fileName="broken.mp3"
        onSaveToFiles={onSave}
        saveBusy={false}
        saveError={null}
      />,
    );

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.forumThreadAttachmentAudioPlay));
    expect(screen.getByText('This sound could not be played.')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.forumThreadAttachmentSaveFile)).toBeOnTheScreen();

    await user.press(screen.getByTestId(testIds.forumThreadAttachmentSaveFile));
    expect(onSave).toHaveBeenCalled();
  });
});
