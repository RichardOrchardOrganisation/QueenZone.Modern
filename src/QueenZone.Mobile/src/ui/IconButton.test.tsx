import { getHostByType } from '../test/renderer';
import { fireEvent, screen } from '@testing-library/react-native';
import { Download } from 'lucide-react-native';
import type { ComponentProps } from 'react';
import { renderWithProviders } from '../test/render';
import { IconButton } from './IconButton';

async function renderButton(
  props: Partial<ComponentProps<typeof IconButton>> = {},
  onPress = jest.fn(),
) {
  await renderWithProviders(
    <IconButton icon={Download} accessibilityLabel="Save to Photos" onPress={onPress} {...props} />,
    { navigation: false },
  );
  return onPress;
}

describe('IconButton', () => {
  it('disables the control and shows a spinner while busy', async () => {
    const onPress = await renderButton({ busy: true });
    const button = screen.getByRole('button', { name: 'Save to Photos' });
    expect(button.props.accessibilityState).toEqual({ disabled: true, busy: true });
    expect(getHostByType('ActivityIndicator')).toBeTruthy();
    await fireEvent.press(button);
    expect(onPress).not.toHaveBeenCalled();
  });

  it('does not fire onPress when disabled', async () => {
    const onPress = await renderButton({ disabled: true });
    const button = screen.getByRole('button', { name: 'Save to Photos' });
    expect(button.props.accessibilityState).toEqual({ disabled: true, busy: false });
    await fireEvent.press(button);
    expect(onPress).not.toHaveBeenCalled();
  });
});
