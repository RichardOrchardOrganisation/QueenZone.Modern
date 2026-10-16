import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchPhotoCategory, fetchPhotoCategoryItems } from '../../api';
import { ApiError } from '../../api/client';
import type { ApiPagedResponse, PhotoCategoryListItem, PhotoListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { PhotoCategoryScreen } from './PhotoCategoryScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchPhotoCategory: jest.fn(),
    fetchPhotoCategoryItems: jest.fn(),
  };
});

const fetchCategory = fetchPhotoCategory as jest.MockedFunction<typeof fetchPhotoCategory>;
const fetchItems = fetchPhotoCategoryItems as jest.MockedFunction<typeof fetchPhotoCategoryItems>;

function categoryFixture(overrides: Partial<PhotoCategoryListItem> = {}): PhotoCategoryListItem {
  return {
    catId: 1,
    name: 'Brian May',
    slug: 'brian-may',
    imageCount: 3,
    coverThumbnailUrl: null,
    detailPath: '/photography/brian-may',
    ...overrides,
  };
}

function photoFixture(overrides: Partial<PhotoListItem> = {}): PhotoListItem {
  return {
    picId: 101,
    catId: 1,
    categoryName: 'Brian May',
    categorySlug: 'brian-may',
    title: 'Live Aid',
    thumbnailUrl: 'https://example.com/brian-may/img-101-t.jpg',
    thumbWidth: 200,
    thumbHeight: 150,
    pictureWidth: 1600,
    pictureHeight: 900,
    pictureDimensionsLabel: '1600 x 900',
    year: 1985,
    dateTime: '1985-07-13T00:00:00.000Z',
    detailPath: '/photography/brian-may/101',
    categoryPath: '/photography/brian-may',
    ...overrides,
  };
}

function photoItemsPage(
  count: number,
  totalCount: number,
  overrides: Partial<PhotoListItem> = {},
): ApiPagedResponse<PhotoListItem> {
  const items = Array.from({ length: count }, (_, index) =>
    photoFixture({
      picId: 101 + index,
      title: index === 0 ? 'Live Aid' : `Photo ${101 + index}`,
      detailPath: `/photography/brian-may/${101 + index}`,
      ...overrides,
    }),
  );
  return {
    items,
    page: 1,
    pageSize: 24,
    totalCount,
    totalPages: Math.max(1, Math.ceil(totalCount / 24)),
  };
}

function querySize(call: unknown[]): string | undefined {
  const query = call[1] as { size?: string } | undefined;
  return query?.size;
}

async function renderPhotoCategory(navigation = fakeNavigation()) {
  return {
    navigation,
    ...(await renderWithProviders(
      <PhotoCategoryScreen
        navigation={navigation as never}
        route={
          {
            key: 'photo-category',
            name: 'PhotoCategory',
            params: { slug: 'brian-may', name: 'Brian May' },
          } as never
        }
      />,
    )),
  };
}

describe('PhotoCategoryScreen', () => {
  beforeEach(() => {
    fetchCategory.mockReset();
    fetchItems.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('loads photos and opens the viewer', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchItems.mockResolvedValue(pagedResponse([photoFixture()]));

    const { navigation } = await renderPhotoCategory();
    await waitFor(() => expect(screen.getByTestId(testIds.photoCategoryScreen)).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen();
    expect(fetchCategory).toHaveBeenCalledWith('brian-may', expect.any(AbortSignal));
    expect(fetchItems).toHaveBeenCalledWith(
      'brian-may',
      expect.objectContaining({ page: 1, pageSize: 24, size: undefined }),
    );

    const user = userEvent.setup();
    expect(screen.queryByRole('button', { name: 'Save to Photos' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Set as wallpaper' })).toBeNull();
    expect(screen.queryByTestId(testIds.photoViewerSave)).toBeNull();
    expect(screen.queryByTestId(testIds.photoViewerWallpaper)).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Live Aid' }));
    expect(navigation.navigate).toHaveBeenCalledWith('PhotoViewer', { slug: 'brian-may', picId: 101 });
  });

  it('shows an error and retries', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchItems
      .mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'))
      .mockResolvedValueOnce(pagedResponse([photoFixture()]));

    await renderPhotoCategory();
    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());
    expect(fetchItems).toHaveBeenCalledTimes(2);
  });

  it('shows empty copy when the collection has no images', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchItems.mockResolvedValue(pagedResponse([], 1, 0));

    await renderPhotoCategory();
    await waitFor(() =>
      expect(screen.getByText('No images are available in this collection yet.')).toBeOnTheScreen(),
    );
  });

  it('keeps the size chip selected and uses the filtered total without resetting', async () => {
    fetchCategory.mockResolvedValue(categoryFixture({ imageCount: 1087 }));
    fetchItems.mockImplementation(async (_slug, query = {}) => {
      if (query.size === 'desktop') {
        return photoItemsPage(24, 412);
      }
      if (query.size === 'phone') {
        return photoItemsPage(0, 0);
      }
      return photoItemsPage(24, 1087);
    });

    const { navigation } = await renderPhotoCategory();
    await waitFor(() => expect(screen.getByText('Showing 1–24 of 1087')).toBeOnTheScreen());
    expect(screen.getByText('1,087 images in the archive')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'All sizes' }).props.accessibilityState).toEqual({
      selected: true,
    });

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Desktop wallpaper' }));

    await waitFor(() => expect(screen.getByText('Showing 1–24 of 412')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Desktop wallpaper' }).props.accessibilityState).toEqual({
      selected: true,
    });
    expect(screen.getByRole('button', { name: 'All sizes' }).props.accessibilityState).toEqual({
      selected: false,
    });
    expect(screen.getByText('1,087 images in the archive')).toBeOnTheScreen();
    expect(querySize(fetchItems.mock.calls[fetchItems.mock.calls.length - 1])).toBe('desktop');
    expect(fetchItems.mock.calls.slice(1).some((call) => querySize(call) === undefined)).toBe(false);

    await user.press(screen.getByRole('button', { name: 'Live Aid' }));
    expect(navigation.navigate).toHaveBeenCalledWith('PhotoViewer', {
      slug: 'brian-may',
      picId: 101,
      size: 'desktop',
    });

    await user.press(screen.getByRole('button', { name: 'All sizes' }));
    await waitFor(() => expect(screen.getByText('Showing 1–24 of 1087')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'All sizes' }).props.accessibilityState).toEqual({
      selected: true,
    });
    expect(querySize(fetchItems.mock.calls[fetchItems.mock.calls.length - 1])).toBeUndefined();
  });

  it('shows empty copy for a size preset with no matches', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchItems.mockImplementation(async (_slug, query = {}) => {
      if (query.size === 'phone') {
        return photoItemsPage(0, 0);
      }
      return pagedResponse([photoFixture()]);
    });

    await renderPhotoCategory();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid' })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Phone wallpaper' }));

    await waitFor(() =>
      expect(screen.getByText('No images match Phone wallpaper.')).toBeOnTheScreen(),
    );
    expect(screen.getByRole('button', { name: 'Phone wallpaper' }).props.accessibilityState).toEqual({
      selected: true,
    });
    expect(screen.queryByRole('button', { name: 'Live Aid' })).toBeNull();
  });
});
