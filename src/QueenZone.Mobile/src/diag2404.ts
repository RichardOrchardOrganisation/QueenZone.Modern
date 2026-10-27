// TEMPORARY #2404 diagnostics. Throwaway branch only; never merge.
import { useEffect } from 'react';
const counts: Record<string, number> = {};
export function useMountLog2404(name: string, extra?: string) {
  useEffect(() => {
    counts[name] = (counts[name] ?? 0) + 1;
    console.log(`QZ2404 mount ${name} n=${counts[name]}${extra ? ' ' + extra : ''}`);
    return () => console.log(`QZ2404 unmount ${name}${extra ? ' ' + extra : ''}`);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}
type S = { routes?: { name: string; key: string; params?: unknown; state?: S }[]; index?: number } | undefined;
export function describeState2404(s: S): string {
  if (!s?.routes) return '';
  return '[' + s.routes.map((r, i) => `${i === s.index ? '*' : ''}${r.name}${r.name === 'CrosswordPlay' ? '(' + ((r.params as { slug?: string })?.slug ?? '') + ',' + r.key.slice(-6) + ')' : ''}${r.state ? describeState2404(r.state as S) : ''}`).join(',') + ']';
}
