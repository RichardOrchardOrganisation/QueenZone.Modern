import { screen } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { ArchiveImage } from './ArchiveImage';

describe('ArchiveImage', () => {
  it('forwards label, recycling key, priority, and invert-colors', () => {
    renderWithProviders(
      <ArchiveImage
        source={{ uri: 'https://cdn.queenzone.org/discography/a-night-at-the-opera.jpg' }}
        label="A Night at the Opera"
        style={{ width: 64, height: 64 }}
        recyclingKey="7"
        priority="low"
        accessibilityIgnoresInvertColors
      />,
      { navigation: false },
    );

    const image = screen.getByLabelText('A Night at the Opera');
    expect(image.props.source).toEqual({
      uri: 'https://cdn.queenzone.org/discography/a-night-at-the-opera.jpg',
    });
    expect(image.props.recyclingKey).toBe('7');
    expect(image.props.priority).toBe('low');
    expect(image.props.cachePolicy).toBe('memory-disk');
    expect(image.props.accessibilityIgnoresInvertColors).toBe(true);
  });

  it('falls back to the URI when recyclingKey is omitted', () => {
    renderWithProviders(
      <ArchiveImage
        source={{ uri: 'https://cdn.queenzone.org/photos/101.jpg' }}
        label="Live Aid"
        style={{ width: 100, height: 100 }}
      />,
      { navigation: false },
    );

    expect(screen.getByLabelText('Live Aid').props.recyclingKey).toBe(
      'https://cdn.queenzone.org/photos/101.jpg',
    );
    expect(screen.getByLabelText('Live Aid').props.priority).toBe('normal');
  });

  it('forwards load callbacks to expo-image', () => {
    const onLoadStart = jest.fn();
    const onLoad = jest.fn();
    const onError = jest.fn();
    renderWithProviders(
      <ArchiveImage
        source={{ uri: 'https://cdn.queenzone.org/photos/101.jpg' }}
        label="Live Aid"
        style={{ width: 100, height: 100 }}
        onLoadStart={onLoadStart}
        onLoad={onLoad}
        onError={onError}
      />,
      { navigation: false },
    );

    const image = screen.getByLabelText('Live Aid');
    expect(image.props.onLoadStart).toBe(onLoadStart);
    expect(image.props.onLoad).toBe(onLoad);
    expect(image.props.onError).toBe(onError);
  });
});
