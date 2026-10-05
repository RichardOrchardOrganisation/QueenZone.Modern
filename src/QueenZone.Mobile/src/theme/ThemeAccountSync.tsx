import { useEffect } from 'react';
import { useSession } from '../session/SessionContext';
import { useTheme } from './ThemeProvider';

/**
 * Applies the signed-in member's saved appearance (the website shares it) to this device.
 * Changes made on the Appearance screen are written back to the account there. Renders nothing.
 */
export function ThemeAccountSync() {
  const { profile } = useSession();
  const { setPreference } = useTheme();
  const accountPreference = profile?.themePreference;

  useEffect(() => {
    if (accountPreference) {
      setPreference(accountPreference);
    }
  }, [accountPreference, setPreference]);

  return null;
}
