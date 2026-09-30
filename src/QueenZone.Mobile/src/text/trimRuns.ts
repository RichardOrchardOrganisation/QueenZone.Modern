/**
 * Linear scans that replace `/x+$/`-style regexes (Sonar typescript:S8786).
 * Production imports use a `.ts` extension so Node's `--experimental-strip-types`
 * test runner can resolve this module the same way colocated `*.test.ts` files do.
 */

/** Drop a run of `char` from the end. Linear, no `/x+$/` backtracking. */
export function trimTrailingChar(value: string, char: string): string {
  const code = char.charCodeAt(0);
  let end = value.length;
  while (end > 0 && value.charCodeAt(end - 1) === code) {
    end -= 1;
  }
  return end === value.length ? value : value.slice(0, end);
}

/** Drop a run of `char` from the start. Linear, no `/^x+/` backtracking. */
export function trimLeadingChar(value: string, char: string): string {
  const code = char.charCodeAt(0);
  let start = 0;
  const { length } = value;
  while (start < length && value.charCodeAt(start) === code) {
    start += 1;
  }
  return start === 0 ? value : value.slice(start);
}

/** Drop a run of `char` from both ends. */
export function trimChar(value: string, char: string): string {
  return trimTrailingChar(trimLeadingChar(value, char), char);
}

/** Drop a run of characters from `chars` at the end. */
export function trimTrailingSet(value: string, chars: ReadonlySet<string>): string {
  let end = value.length;
  while (end > 0 && chars.has(value[end - 1]!)) {
    end -= 1;
  }
  return end === value.length ? value : value.slice(0, end);
}

/** Base64url alphabet plus stripped padding. Replaces `/=+$/` on token bytes. */
export function toUrlSafeBase64(value: string): string {
  return trimTrailingChar(value.replaceAll('+', '-').replaceAll('/', '_'), '=');
}
