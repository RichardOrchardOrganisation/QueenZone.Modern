import { screen, userEvent } from '@testing-library/react-native';
import type { ForumPoll } from '../../api';
import { renderWithProviders } from '../../test/render';
import { ForumPollCard } from './ForumPollCard';

function pollFixture(overrides: Partial<ForumPoll> = {}): ForumPoll {
  return {
    pollId: 'poll-1',
    topicId: 1002,
    question: 'Best studio album?',
    isMultiChoice: false,
    maxChoices: null,
    closesAt: null,
    closedAt: null,
    createdAt: '2024-01-01T00:00:00.000Z',
    totalVotes: 0,
    distinctVoters: 0,
    viewerHasVoted: false,
    isClosed: false,
    canViewerVote: true,
    canViewerClose: false,
    options: [
      {
        optionId: 'opt-a',
        optionText: 'A Night at the Opera',
        displayOrder: 1,
        voteCount: 0,
        percentage: 0,
        selectedByViewer: false,
      },
      {
        optionId: 'opt-b',
        optionText: 'Innuendo',
        displayOrder: 2,
        voteCount: 0,
        percentage: 0,
        selectedByViewer: false,
      },
      {
        optionId: 'opt-c',
        optionText: 'The Game',
        displayOrder: 3,
        voteCount: 0,
        percentage: 0,
        selectedByViewer: false,
      },
    ],
    ...overrides,
  };
}

async function renderCard(
  poll: ForumPoll,
  handlers: {
    onVote?: (optionIds: string[]) => void;
    onClose?: () => void;
    onSignIn?: () => void;
    isSignedIn?: boolean;
    hasAccessToken?: boolean;
    busy?: boolean;
  } = {},
) {
  const onVote = handlers.onVote ?? jest.fn();
  const onClose = handlers.onClose ?? jest.fn();
  const onSignIn = handlers.onSignIn ?? jest.fn();
  await renderWithProviders(
    <ForumPollCard
      poll={poll}
      isSignedIn={handlers.isSignedIn ?? true}
      hasAccessToken={handlers.hasAccessToken ?? true}
      busy={handlers.busy ?? false}
      error={null}
      onVote={onVote}
      onClose={onClose}
      onSignIn={onSignIn}
    />,
    { navigation: false },
  );
  return { onVote, onClose, onSignIn };
}

describe('ForumPollCard', () => {
  it('selects an option and votes', async () => {
    const { onVote } = await renderCard(pollFixture());
    const user = userEvent.setup();
    await user.press(screen.getByRole('radio', { name: 'A Night at the Opera' }));
    await user.press(screen.getByRole('button', { name: 'Vote' }));
    expect(onVote).toHaveBeenCalledWith(['opt-a']);
  });

  it('does not select more than the multi-choice max', async () => {
    const { onVote } = await renderCard(pollFixture({ isMultiChoice: true, maxChoices: 2 }));
    const user = userEvent.setup();
    await user.press(screen.getByRole('checkbox', { name: 'A Night at the Opera' }));
    await user.press(screen.getByRole('checkbox', { name: 'Innuendo' }));
    await user.press(screen.getByRole('checkbox', { name: 'The Game' }));
    await user.press(screen.getByRole('button', { name: 'Vote' }));
    expect(onVote).toHaveBeenCalledWith(['opt-a', 'opt-b']);
  });

  it('prompts unsigned visitors to sign in', async () => {
    const { onSignIn, onVote } = await renderCard(pollFixture(), {
      isSignedIn: false,
      hasAccessToken: false,
    });
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Sign in' }));
    expect(onSignIn).toHaveBeenCalled();
    expect(onVote).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Vote' })).toBeNull();
  });

  it('closes the poll when the viewer can close it', async () => {
    const { onClose } = await renderCard(pollFixture({ canViewerClose: true }));
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Close poll' }));
    expect(onClose).toHaveBeenCalled();
  });
});
