import { fireEvent, screen } from '@testing-library/react-native';
import { Download } from 'lucide-react-native';
import type { ComponentProps } from 'react';
import { ActivityIndicator } from 'react-native';
import { renderWithProviders } from '../test/render';
import { IconButton } from './IconButton';

function renderButton(
  props: Partial<ComponentProps<typeof IconButton>> = {},
  onPress = jest.fn(),
) {
  renderWithProviders(
    <IconButton icon={Download} accessibilityLabel="Save to Photos" onPress={onPress} {...props} />,
    { navigation: false },
  );
  return onPress;
}

describe('IconButton', () => {
  it('disables the control and shows a spinner while busy', () => {
    const onPress = renderButton({ busy: true });
    const button = screen.getByRole('button', { name: 'Save to Photos' });
    expect(button.props.accessibilityState).toEqual({ disabled: true, busy: true });
    expect(screen.UNSAFE_getByType(ActivityIndicator)).toBeTruthy();
    fireEvent.press(button);
    expect(onPress).not.toHaveBeenCalled();
  });

  it('does not fire onPress when disabled', () => {
    const onPress = renderButton({ disabled: true });
    const button = screen.getByRole('button', { name: 'Save to Photos' });
    expect(button.props.accessibilityState).toEqual({ disabled: true, busy: false });
    fireEvent.press(button);
    expect(onPress).not.toHaveBeenCalled();
  });
});
