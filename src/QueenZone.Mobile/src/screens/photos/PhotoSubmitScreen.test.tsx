import { screen, userEvent, waitFor } from '@testing-library/react-native';
import * as ImagePicker from 'expo-image-picker';
import { Keyboard } from 'react-native';
import { fetchPhotoCategories } from '../../api';
import { ApiError } from '../../api/errors';
import { createPhotoSubmission } from '../../api/photoSubmissions';
import type { PhotoCategoryListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { PhotoSubmitScreen } from './PhotoSubmitScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return {
    ...actual,
    useNavigation: () => ({ navigate: jest.fn() }),
  };
});

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchPhotoCategories: jest.fn(),
  };
});

jest.mock('../../api/photoSubmissions', () => {
  const actual = jest.requireActual('../../api/photoSubmissions');
  return {
    ...actual,
    createPhotoSubmission: jest.fn(),
  };
});

jest.mock('expo-image-picker', () => ({
  requestMediaLibraryPermissionsAsync: jest.fn(),
  requestCameraPermissionsAsync: jest.fn(),
  launchImageLibraryAsync: jest.fn(),
  launchCameraAsync: jest.fn(),
  UIImagePickerPreferredAssetRepresentationMode: { Compatible: 'compatible' },
}));

const fetchCategories = fetchPhotoCategories as jest.MockedFunction<typeof fetchPhotoCategories>;
const submitPhoto = createPhotoSubmission as jest.MockedFunction<typeof createPhotoSubmission>;

async function renderSubmit() {
  return await renderWithProviders(
    <PhotoSubmitScreen
      navigation={fakeNavigation() as never}
      route={{ key: 'submit', name: 'PhotoSubmit' } as never}
    />,
  );
}

describe('PhotoSubmitScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    fetchCategories.mockResolvedValue(pagedResponse([], 1, 0));
    submitPhoto.mockReset();
  });

  it('gates unsigned visitors', async () => {
    await renderSubmit();
    expect(screen.getByText('Submit a photo')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  });

  it('validates a missing photo and reports library permission denial', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: false });
    await renderSubmit();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Submit for review' })).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'Submit for review' }));
    expect(screen.getByText('Title is required.')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Choose from library' }));
    await waitFor(() =>
      expect(screen.getByText('Photo library permission is required to choose a photo.')).toBeOnTheScreen(),
    );
  });

  it('shows a local-file error instead of a fake offline message', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchCategories.mockResolvedValue(pagedResponse([categoryFixture()], 1, 1));
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchImageLibraryAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/crop.jpg', fileName: 'crop.jpg', mimeType: 'image/jpeg', fileSize: 12_000 }],
    });
    submitPhoto.mockRejectedValueOnce(ApiError.localFile(new TypeError('Network request failed')));

    await renderSubmit();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Brian May' })).toBeOnTheScreen());
    await user.type(screen.getByLabelText('Title'), 'Fan pic');
    await user.press(screen.getByRole('button', { name: 'Brian May' }));
    await user.press(screen.getByRole('button', { name: 'Choose from library' }));
    await user.press(screen.getByRole('button', { name: 'Submit for review' }));

    await waitFor(() =>
      expect(screen.getByText('Could not read the selected photo. Try choosing it again.')).toBeOnTheScreen(),
    );
    expect(screen.queryByText('Unable to reach QueenZone. Check your connection and try again.')).toBeNull();
  });

  it('omits the free-text category field and submits a chip name', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchCategories.mockResolvedValue(pagedResponse([categoryFixture()], 1, 1));
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchImageLibraryAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/crowd.jpg', fileName: 'crowd.jpg', mimeType: 'image/jpeg', fileSize: 12_000 }],
    });
    submitPhoto.mockResolvedValueOnce({
      id: 'sub-1',
      status: 'Pending',
      title: 'Wembley',
      submittedAt: '2026-08-23T00:15:00.000Z',
    });

    await renderSubmit();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Brian May' })).toBeOnTheScreen());
    expect(screen.getByText('Category')).toBeOnTheScreen();
    expect(screen.queryByText('Suggested category (optional)')).toBeNull();
    expect(screen.queryByLabelText('Suggested category')).toBeNull();
    expect(screen.queryByPlaceholderText('Category name')).toBeNull();

    await user.type(screen.getByLabelText('Title'), 'Wembley');
    await user.press(screen.getByRole('button', { name: 'Brian May' }));
    await user.press(screen.getByRole('button', { name: 'Choose from library' }));
    await user.press(screen.getByRole('button', { name: 'Submit for review' }));

    await waitFor(() => expect(submitPhoto).toHaveBeenCalledTimes(1));
    expect(submitPhoto).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Wembley',
        suggestedCategory: 'Brian May',
        photo: expect.objectContaining({ name: 'crowd.jpg', type: 'image/jpeg' }),
      }),
      'tok',
    );
  });

  it('blocks submit when no chip is selected and clears the error after a chip is chosen', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchCategories.mockResolvedValue(pagedResponse([categoryFixture()], 1, 1));
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchImageLibraryAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/crowd.jpg', fileName: 'crowd.jpg', mimeType: 'image/jpeg', fileSize: 12_000 }],
    });
    submitPhoto.mockResolvedValueOnce({
      id: 'sub-2',
      status: 'Pending',
      title: 'Fan pic',
      submittedAt: '2026-08-23T00:15:00.000Z',
    });

    await renderSubmit();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Brian May' })).toBeOnTheScreen());
    await user.type(screen.getByLabelText('Title'), 'Fan pic');
    await user.press(screen.getByRole('button', { name: 'Choose from library' }));
    await user.press(screen.getByRole('button', { name: 'Submit for review' }));

    expect(submitPhoto).not.toHaveBeenCalled();
    expect(screen.getByText('Select a category.')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Brian May' }));
    expect(screen.queryByText('Select a category.')).toBeNull();

    await user.press(screen.getByRole('button', { name: 'Submit for review' }));
    await waitFor(() => expect(submitPhoto).toHaveBeenCalledTimes(1));
    expect(submitPhoto).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Fan pic',
        suggestedCategory: 'Brian May',
      }),
      'tok',
    );
  });

  it('dismisses the keyboard after a library pick so submit stays reachable', async () => {
    const user = userEvent.setup();
    const dismiss = jest.spyOn(Keyboard, 'dismiss');
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchCategories.mockResolvedValue(pagedResponse([categoryFixture()], 1, 1));
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchImageLibraryAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/crowd.jpg', fileName: 'crowd.jpg', mimeType: 'image/jpeg', fileSize: 12_000 }],
    });

    await renderSubmit();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Brian May' })).toBeOnTheScreen());
    await user.type(screen.getByLabelText('Title'), 'Wembley');
    await user.press(screen.getByRole('button', { name: 'Choose from library' }));

    await waitFor(() => expect(dismiss).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: 'Submit for review' })).toBeOnTheScreen();
    dismiss.mockRestore();
  });

  it('dismisses the keyboard after a camera pick so submit stays reachable', async () => {
    const user = userEvent.setup();
    const dismiss = jest.spyOn(Keyboard, 'dismiss');
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchCategories.mockResolvedValue(pagedResponse([categoryFixture()], 1, 1));
    (ImagePicker.requestCameraPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchCameraAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/stage.jpg', fileName: 'stage.jpg', mimeType: 'image/jpeg', fileSize: 12_000 }],
    });

    await renderSubmit();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Brian May' })).toBeOnTheScreen());
    await user.type(screen.getByLabelText('Title'), 'Live');
    await user.press(screen.getByRole('button', { name: 'Take photo' }));

    await waitFor(() => expect(dismiss).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: 'Submit for review' })).toBeOnTheScreen();
    dismiss.mockRestore();
  });

  it('shows a retry action when photo categories fail to load', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchCategories
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(pagedResponse([categoryFixture()], 1, 1));

    await renderSubmit();
    await waitFor(() => expect(screen.getByText('Could not load photo categories.')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Submit for review' })).toBeDisabled();

    await user.press(screen.getByRole('button', { name: 'Retry loading photo categories' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Brian May' })).toBeOnTheScreen());
    expect(screen.queryByText('Could not load photo categories.')).toBeNull();
    expect(screen.getByRole('button', { name: 'Submit for review' })).toBeEnabled();
  });
});

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
