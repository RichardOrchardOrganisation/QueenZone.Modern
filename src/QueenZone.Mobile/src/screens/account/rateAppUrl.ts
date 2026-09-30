export function rateAppUrl(platform: string): string | null {
  if (platform === 'ios') {
    return 'https://apps.apple.com/app/apple-store/id6803889011?action=write-review';
  }
  if (platform === 'android') {
    return 'https://play.google.com/store/apps/details?id=org.queenzone.mobile&showAllReviews=true';
  }
  return null;
}
