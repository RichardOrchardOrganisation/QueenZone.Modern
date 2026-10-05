import { useEffect } from 'react';
import { useSession } from '../session/SessionContext';
import { useTheme } from './ThemeProvider';

/**
 * Makes the signed-in member's saved appearance (the website shares it) the account-level choice.
 * A device override chosen on the Appearance screen still wins. Signing out drops back to the system
 * setting. Renders nothing.
 */
export function ThemeAccountSync() {
  const { profile } = useSession();
  const { setAccountPreference } = useTheme();
  const accountPreference = profile?.themePreference ?? 'system';

  useEffect(() => {
    setAccountPreference(accountPreference);
  }, [accountPreference, setAccountPreference]);

  return null;
}
