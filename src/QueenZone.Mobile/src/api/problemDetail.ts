export function readProblemDetail(payload: unknown, fallback: string): string {
  if (!payload || typeof payload !== 'object') {
    return fallback;
  }

  const detail = (payload as { detail?: unknown }).detail;
  if (typeof detail === 'string' && detail.trim().length > 0) {
    return detail.trim();
  }

  const title = (payload as { title?: unknown }).title;
  if (typeof title === 'string' && title.trim().length > 0) {
    return title.trim();
  }

  return fallback;
}
