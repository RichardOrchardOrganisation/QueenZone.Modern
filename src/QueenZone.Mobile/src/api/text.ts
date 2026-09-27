/** Strip simple HTML tags for plain-text fallbacks (e.g. RichHtmlBody error path). */
export function toPlainText(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  let text = value
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<\/p>/gi, '\n\n');

  // Repeat until stable so nested markup cannot reappear after one pass
  // (CodeQL js/incomplete-multi-character-sanitization).
  let previous: string;
  do {
    previous = text;
    text = stripHtmlTags(text);
  } while (text !== previous);
  text = text.replace(/[<>]/g, '');

  // Decode safe entities only. Leave &lt;/&gt; encoded so angle brackets are
  // never reintroduced. Decode &amp; last to avoid double-unescaping
  // (CodeQL js/double-escaping).
  return text
    .replace(/&nbsp;/gi, ' ')
    .replace(/&quot;/gi, '"')
    .replace(/&#39;/gi, "'")
    .replace(/&amp;/gi, '&')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

/** Linear scan equivalent of `/<[^>]*>/g` — first `>` after each `<` closes the tag. */
function stripHtmlTags(value: string): string {
  let result = '';
  let index = 0;
  while (index < value.length) {
    const open = value.indexOf('<', index);
    if (open === -1) {
      return result + value.slice(index);
    }
    result += value.slice(index, open);
    const close = value.indexOf('>', open + 1);
    if (close === -1) {
      return result + value.slice(open);
    }
    index = close + 1;
  }
  return result;
}

export function formatPublishedDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}
