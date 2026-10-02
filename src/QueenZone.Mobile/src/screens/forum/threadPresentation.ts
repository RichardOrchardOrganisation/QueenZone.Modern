import type { ForumTopicDetail } from '../../api/types';

export function threadHeading(topic: Pick<ForumTopicDetail, 'title' | 'forumName' | 'postCount'> | null, routeTitle: string | undefined) {
  return {
    forumName: topic?.forumName ?? 'Forum',
    title: topic?.title ?? routeTitle ?? 'Thread',
    stats: topic ? `${topic.postCount.toLocaleString()} posts · ${topic.forumName}` : null,
  };
}
