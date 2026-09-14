export type WallpaperTarget = 'home' | 'lock' | 'both';

export const wallpaperCopy = {
  accessibilityLabel: 'Set as wallpaper',
  home: 'Home screen',
  lock: 'Lock screen',
  both: 'Both',
  cancel: 'Cancel',
  iosSaved: 'Saved to Photos. Set it as wallpaper in Settings → Wallpaper.',
  iosAlreadySaved: 'Already saved — set it in Settings → Wallpaper.',
  androidSet: 'Wallpaper set.',
  androidAlreadySet: 'Wallpaper already set.',
  homeFailed: 'Unable to set the home screen wallpaper.',
  lockFailed: 'Unable to set the lock screen wallpaper.',
  bothFailed: 'Unable to set the wallpaper.',
  unsupported: 'Setting wallpaper is not available on this device.',
  androidOnly: 'Setting wallpaper is only available on Android.',
} as const;

export const wallpaperTargets: readonly { target: WallpaperTarget; label: string }[] = [
  { target: 'home', label: wallpaperCopy.home },
  { target: 'lock', label: wallpaperCopy.lock },
  { target: 'both', label: wallpaperCopy.both },
];

export function wallpaperFailureMessage(target: WallpaperTarget, nativeMessage = ''): string {
  if (nativeMessage === 'unsupported') {
    return wallpaperCopy.unsupported;
  }
  switch (target) {
    case 'home':
      return wallpaperCopy.homeFailed;
    case 'lock':
      return wallpaperCopy.lockFailed;
    case 'both':
      return wallpaperCopy.bothFailed;
  }
}

export function claimsWallpaperSet(message: string): boolean {
  return /wallpaper set/i.test(message);
}
