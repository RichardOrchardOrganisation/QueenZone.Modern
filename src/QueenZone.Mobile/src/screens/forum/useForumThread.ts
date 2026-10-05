import { useCallback, useEffect, useRef, useState } from 'react';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import {
  ApiError,
  closeForumTopicPoll,
  fetchForumTopicPoll,
  fetchForumTopicResult,
  fetchForumTopicWatch,
  isOfflineFailure,
  isTimeoutFailure,
  unwatchForumTopic,
  voteForumTopicPoll,
  watchForumTopic,
  type CacheSource,
  type ForumPoll,
  type ForumTopicDetail,
} from '../../api';
import type { ForumStackParamList } from '../../navigation/types';
import { openSignIn } from '../../session/signInNavigation';
import { pollActionErrorMessage, pollTokenRequiredMessage, shouldLoadPoll } from './forumPollMeta';

function messageFromUnknownError(err: unknown): string {
  return err instanceof ApiError ? err.message : 'Something went wrong.';
}

function isStaleReadFailure(err: unknown): boolean {
  return isOfflineFailure(err) || isTimeoutFailure(err);
}

type ForumNavigation = NativeStackNavigationProp<ForumStackParamList, 'Thread'>;

/** Owns topic + watch + poll only — posts stay on `usePagedContent` in `ThreadScreen`. */
export function useForumThread(id: number | null, accessToken: string | null, navigation: ForumNavigation) {
  const [topic, setTopic] = useState<ForumTopicDetail | null>(null);
  const [topicError, setTopicError] = useState<string | null>(null);
  const [topicReloadToken, setTopicReloadToken] = useState(0);
  const [topicSource, setTopicSource] = useState<CacheSource>('network');
  const [topicCachedAt, setTopicCachedAt] = useState<string | null>(null);
  const [poll, setPoll] = useState<ForumPoll | null>(null);
  const [pollBusy, setPollBusy] = useState(false);
  const [pollError, setPollError] = useState<string | null>(null);
  const [watching, setWatching] = useState(false);
  const [watchBusy, setWatchBusy] = useState(false);
  const [watchError, setWatchError] = useState<string | null>(null);
  const topicRef = useRef<ForumTopicDetail | null>(null);
  const topicNetworkOnlyRef = useRef(false);
  topicRef.current = topic;

  useEffect(() => {
    if (id === null) {
      setTopic(null);
      setPoll(null);
      setTopicError('This discussion is not available in the archive yet.');
      return;
    }
    const controller = new AbortController();
    const networkOnly = topicNetworkOnlyRef.current;
    topicNetworkOnlyRef.current = false;
    setTopicError(null);
    async function loadWatch(topicId: number): Promise<boolean> {
      if (accessToken) {
        try {
          const watch = await fetchForumTopicWatch(topicId, accessToken, controller.signal);
          setWatching(watch.watching);
          setWatchError(null);
        } catch (err: unknown) {
          if (err instanceof Error && err.name === 'AbortError') {
            return false;
          }
          setWatching(false);
          if (!(err instanceof ApiError && err.status === 404)) {
            setWatchError(messageFromUnknownError(err));
          }
        }
      } else {
        setWatching(false);
        setWatchError(null);
      }
      return true;
    }

    fetchForumTopicResult(id, controller.signal, { networkOnly })
      .then(async (result) => {
        setTopic(result.data);
        setTopicSource(result.source);
        setTopicCachedAt(result.cachedAt);
        if (result.source === 'cache') {
          setWatching(false);
          setWatchError(null);
          setPoll(null);
          setPollError(null);
          return;
        }
        if (!await loadWatch(id)) return;
        if (!shouldLoadPoll(result.data.hasPoll)) {
          setPoll(null);
          setPollError(null);
          return;
        }
        try {
          const nextPoll = await fetchForumTopicPoll(id, accessToken, controller.signal);
          setPoll(nextPoll);
          setPollError(null);
        } catch (err: unknown) {
          if (err instanceof Error && err.name === 'AbortError') {
            return;
          }
          setPoll(null);
          if (err instanceof ApiError && err.status === 404) {
            setPollError(null);
            return;
          }
          setPollError(messageFromUnknownError(err));
        }
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        if (networkOnly && topicRef.current && isStaleReadFailure(err)) {
          setTopicSource('cache');
          return;
        }
        setTopic(null);
        setPoll(null);
        setTopicError(messageFromUnknownError(err));
      });
    return () => controller.abort();
  }, [accessToken, id, topicReloadToken]);

  const retryTopic = useCallback(() => {
    topicNetworkOnlyRef.current = false;
    setTopicReloadToken((n) => n + 1);
  }, []);

  const refreshTopic = useCallback(() => {
    topicNetworkOnlyRef.current = true;
    setTopicReloadToken((n) => n + 1);
  }, []);

  const runPollAction = useCallback(
    async (action: () => Promise<ForumPoll>) => {
      if (id === null) {
        return;
      }
      setPollBusy(true);
      setPollError(null);
      try {
        setPoll(await action());
      } catch (err: unknown) {
        setPollError(pollActionErrorMessage(err));
      } finally {
        setPollBusy(false);
      }
    },
    [id],
  );

  const votePoll = useCallback(
    (optionIds: string[]) => {
      if (id === null || !accessToken) {
        setPollError(accessToken ? 'Something went wrong.' : pollTokenRequiredMessage);
        return;
      }
      void runPollAction(() => voteForumTopicPoll(id, optionIds, accessToken));
    },
    [accessToken, id, runPollAction],
  );

  const closePoll = useCallback(() => {
    if (id === null || !accessToken) {
      setPollError('Closing a poll requires a mobile Bearer token.');
      return;
    }
    void runPollAction(() => closeForumTopicPoll(id, accessToken));
  }, [accessToken, id, runPollAction]);

  const toggleWatch = useCallback(() => {
    if (id === null) {
      return;
    }
    if (!accessToken) {
      openSignIn(navigation);
      return;
    }
    setWatchBusy(true);
    setWatchError(null);
    const action = watching ? unwatchForumTopic(id, accessToken) : watchForumTopic(id, accessToken);
    void action
      .then((next) => setWatching(next.watching))
      .catch((err: unknown) => setWatchError(messageFromUnknownError(err)))
      .finally(() => setWatchBusy(false));
  }, [accessToken, id, navigation, watching]);

  return {
    topic,
    topicError,
    topicSource,
    topicCachedAt,
    poll,
    pollBusy,
    pollError,
    watching,
    watchBusy,
    watchError,
    retryTopic,
    refreshTopic,
    votePoll,
    closePoll,
    toggleWatch,
  };
}
