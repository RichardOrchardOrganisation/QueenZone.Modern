import { useEffect, useState } from 'react';
import { Text } from 'react-native';
import { testIds } from '../test/testIds';
import { type, useTheme } from '../theme';
import * as core from './core';
import type { CrosswordTimer } from './core';

/** Used only by CrosswordTimerText so a 1s tick cannot re-render the solver. */
export function useElapsedSeconds(timer: CrosswordTimer) {
  const [seconds, setSeconds] = useState(() => Math.floor(timer.elapsedMs / 1000));
  useEffect(() => {
    setSeconds(core.elapsedSeconds(timer, Date.now()));
    const tick = setInterval(() => setSeconds(core.elapsedSeconds(timer, Date.now())), 1000);
    return () => clearInterval(tick);
  }, [timer]);
  return seconds;
}

export function CrosswordTimerText({ timer }: { timer: CrosswordTimer }) {
  const { c } = useTheme();
  const seconds = useElapsedSeconds(timer);
  return <Text testID={testIds.crosswordTimer} style={[type.meta, { color: c.textPrimary }]}>{Math.floor(seconds / 60)}:{String(seconds % 60).padStart(2, '0')}</Text>;
}
