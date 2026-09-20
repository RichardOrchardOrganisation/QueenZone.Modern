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
  const tasksRef = useRef(tasks);
  tasksRef.current = tasks;

  useEffect(() => {
    return () => {
      epochRef.current += 1;
    };
  }, []);

  const onRefresh = useCallback(() => {
    const epoch = ++epochRef.current;
    const startedAt = Date.now();
    setRefreshing(true);
    void Promise.allSettled(tasksRef.current.map((run) => run()))
      .then(() => waitForMinimumRefreshVisibility(startedAt))
      .then(() => {
        if (epoch === epochRef.current) {
          setRefreshing(false);
        }
      });
  }, []);

  return { refreshing, onRefresh };
}
