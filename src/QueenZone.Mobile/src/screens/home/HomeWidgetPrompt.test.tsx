import AsyncStorage from '@react-native-async-storage/async-storage';
import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderWithProviders } from '../../test/render';
import { HomeWidgetPrompt, widgetPromptDismissedKey } from './HomeWidgetPrompt';

describe('HomeWidgetPrompt', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('explains how to add the widget and remembers dismissal', async () => {
    const { unmount } = renderWithProviders(<HomeWidgetPrompt summary="Queen released The Game." />, { navigation: false });

    await waitFor(() => expect(screen.getByText('A little Queen on your Home Screen')).toBeOnTheScreen());
    fireEvent.press(screen.getByRole('button', { name: 'How to add it' }));
    expect(screen.getByText('2. Tap Edit, then Add Widget.')).toBeOnTheScreen();
    fireEvent.press(screen.getByRole('button', { name: 'Done' }));
    expect(screen.queryByText('Add the QueenZone widget')).toBeNull();

    fireEvent.press(screen.getByRole('button', { name: 'Dismiss widget tip' }));
    await waitFor(() => expect(AsyncStorage.getItem(widgetPromptDismissedKey)).resolves.toBe('true'));
    unmount();

    renderWithProviders(<HomeWidgetPrompt summary="Queen released The Game." />, { navigation: false });
    await waitFor(() => expect(screen.queryByText('A little Queen on your Home Screen')).toBeNull());
  });
});
