import * as Notifications from 'expo-notifications';

/**
 * Foreground receipts use the in-app banner (see NotificationBridge).
 * The OS still lists the notification so a later tap from Notification Center
 * goes through the same #757 deep-link path as a background tap.
 */
export const foregroundNotificationBehavior = {
  shouldShowBanner: false,
  shouldShowList: true,
  shouldPlaySound: false,
  shouldSetBadge: false,
} as const;

export function configureForegroundNotificationHandler(): void {
  Notifications.setNotificationHandler({
    handleNotification: () => Promise.resolve(foregroundNotificationBehavior),
  });
}
