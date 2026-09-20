import { useCallback, useEffect, useRef, useState } from 'react';
import { waitForMinimumRefreshVisibility } from './refreshVisibility';

export type PullToRefreshHandle = {
  refreshing: boolean;
  onRefresh: () => void;
};

export function usePullToRefresh(
  tasks: readonly (() => Promise<void>)[],
): PullToRefreshHandle {
  const [refreshing, setRefreshing] = useState(false);
  const epochRef = useRef(0);
  const abortRef = useRef<AbortController | null>(null);
  const tasksRef = useRef(tasks);
  tasksRef.current = tasks;

  useEffect(() => {
    return () => {
      epochRef.current += 1;
      abortRef.current?.abort();
    };
  }, []);

  const onRefresh = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    const epoch = ++epochRef.current;
    const startedAt = Date.now();
    setRefreshing(true);
    void Promise.allSettled(tasksRef.current.map((run) => run()))
      .then(() => waitForMinimumRefreshVisibility(startedAt, controller.signal))
      .then(() => {
        if (epoch === epochRef.current) {
          setRefreshing(false);
        }
      });
  }, []);

  return { refreshing, onRefresh };
}
