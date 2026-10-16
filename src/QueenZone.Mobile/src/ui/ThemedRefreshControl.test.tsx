import { componentProps } from '../test/renderer';
import * as Haptics from 'expo-haptics';
import { RefreshControl } from 'react-native';
import { act } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { dark, light, palette, ThemeProvider } from '../theme';
import { ThemedRefreshControl } from './ThemedRefreshControl';

jest.mock('expo-haptics', () => ({
  selectionAsync: jest.fn(async () => {}),
}));

const selectionAsync = jest.mocked(Haptics.selectionAsync);

async function renderControl(preference: 'dark' | 'light', onRefresh = jest.fn(), refreshing = false) {
  await renderWithProviders(
    <ThemeProvider preference={preference}>
      <ThemedRefreshControl refreshing={refreshing} onRefresh={onRefresh} />
    </ThemeProvider>,
    { navigation: false },
  );
  return { onRefresh, control: componentProps(RefreshControl) };
}

describe('ThemedRefreshControl', () => {
  it('owns dark iOS tint and Android spinner colours that contrast on surfacePage', async () => {
    const { control } = await renderControl('dark', jest.fn(), true);
    expect(control.props.refreshing).toBe(true);
    expect(control.props.tintColor).toBe(dark.accentPrimary);
    expect(control.props.colors).toEqual([dark.accentPrimary]);
    expect(control.props.progressBackgroundColor).toBe(palette.grey800);
    expect(control.props.progressBackgroundColor).not.toBe(dark.surfacePage);
  });

  it('owns light iOS tint and Android spinner colours that contrast on surfacePage', async () => {
    const { control } = await renderControl('light');
    expect(control.props.tintColor).toBe(light.accentPrimary);
    expect(control.props.colors).toEqual([light.accentPrimary]);
    expect(control.props.progressBackgroundColor).toBe(palette.grey100);
    expect(control.props.progressBackgroundColor).not.toBe(light.surfacePage);
  });

  it('provides subtle haptic feedback and forwards onRefresh', async () => {
    const onRefresh = jest.fn();
    const { control } = await renderControl('dark', onRefresh, false);
    expect(control.props.refreshing).toBe(false);
    await act(() => control.props.onRefresh!());
    expect(selectionAsync).toHaveBeenCalledTimes(1);
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it('still refreshes when haptic feedback is unavailable', async () => {
    selectionAsync.mockRejectedValueOnce(new Error('Haptics unavailable'));
    const onRefresh = jest.fn();
    const { control } = await renderControl('dark', onRefresh, false);

    await act(() => control.props.onRefresh!());

    expect(onRefresh).toHaveBeenCalledTimes(1);
  });
});
