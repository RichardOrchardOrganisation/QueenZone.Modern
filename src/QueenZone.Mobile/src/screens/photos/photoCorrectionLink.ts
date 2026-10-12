import { websiteUrl } from '../archive/searchNavigation.ts';

export const photoCorrectionCopy = {
  label: 'Suggest a correction',
  hint: 'Opens the QueenZone website, where you can sign in and send your proposal to the editors.',
  failed: 'Could not open the website. Try again later.',
} as const;

/**
 * The correction form lives on the website (the canonical contribution form). The app hands off
 * to it rather than duplicating it; the website handles sign-in and shows the pending state.
 */
export function photoCorrectionUrl(apiBaseUrl: string, picId: number): string | null {
  if (!Number.isInteger(picId) || picId <= 0) {
    return null;
  }
  return websiteUrl(apiBaseUrl, `/submit/photo-correction/${picId}`);
}
