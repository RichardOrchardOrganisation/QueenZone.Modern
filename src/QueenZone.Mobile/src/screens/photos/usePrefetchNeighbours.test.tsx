import { act, renderHook, waitFor } from '@testing-library/react-native';
import { Image } from 'expo-image';
import { usePrefetchNeighbours } from './usePrefetchNeighbours';

const prefetch = Image.prefetch as jest.MockedFunction<typeof Image.prefetch>;

const previousUri = 'https://cdn.queenzone.org/brian-may/img-100.jpg';
const currentUri = 'https://cdn.queenzone.org/brian-may/img-101.jpg';
const nextUri = 'https://cdn.queenzone.org/brian-may/img-102.jpg';

describe('usePrefetchNeighbours', () => {
  beforeEach(() => {
    prefetch.mockReset();
    prefetch.mockResolvedValue(true);
  });

  it('does not prefetch before load or on a stale uri', () => {
    const { result } = renderHook(() => usePrefetchNeighbours(currentUri, [previousUri, nextUri]));
    expect(prefetch).not.toHaveBeenCalled();
    act(() => {
      result.current('https://cdn.queenzone.org/brian-may/img-099.jpg');
    });
    expect(prefetch).not.toHaveBeenCalled();
  });

  it('prefetches both neighbour URIs with memory-disk after the current load', async () => {
    const { result } = renderHook(() => usePrefetchNeighbours(currentUri, [previousUri, nextUri]));
    act(() => {
      result.current(currentUri);
    });
    expect(prefetch).toHaveBeenCalledTimes(1);
    expect(prefetch).toHaveBeenCalledWith([previousUri, nextUri], 'memory-disk');
    act(() => {
      result.current(currentUri);
    });
    expect(prefetch).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(prefetch).toHaveBeenCalledTimes(1));
  });

  it('skips a null neighbour and a missing image URL', () => {
    const { result } = renderHook(() =>
      usePrefetchNeighbours(currentUri, [previousUri, null, undefined, '']),
    );
    act(() => {
      result.current(currentUri);
    });
    expect(prefetch).toHaveBeenCalledWith([previousUri], 'memory-disk');
  });

  it('does not prefetch when no neighbour URIs exist', () => {
    const { result } = renderHook(() => usePrefetchNeighbours(currentUri, [null, undefined]));
    act(() => {
      result.current(currentUri);
    });
    expect(prefetch).not.toHaveBeenCalled();
  });

  it('swallows a rejected prefetch', async () => {
    prefetch.mockRejectedValueOnce(new Error('offline'));
    const { result } = renderHook(() => usePrefetchNeighbours(currentUri, [nextUri]));
    act(() => {
      result.current(currentUri);
    });
    await waitFor(() => expect(prefetch).toHaveBeenCalledTimes(1));
  });
});
