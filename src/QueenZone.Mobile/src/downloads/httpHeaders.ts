export function parseContentRangeTotal(header: string | null): number | null {
  if (!header) {
    return null;
  }
  const match = /\/(\d+)\s*$/.exec(header);
  if (!match?.[1]) {
    return null;
  }
  const total = Number(match[1]);
  return Number.isFinite(total) && total > 0 ? total : null;
}

