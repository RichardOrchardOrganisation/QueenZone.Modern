import { screen, waitFor } from '@testing-library/react-native';
import { fetchBiographyChapter } from '../../api';
import type { BiographyChapterDetail } from '../../api/types';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { BiographyChapterScreen } from './BiographyChapterScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchBiographyChapter: jest.fn(),
  };
});

const fetchChapter = fetchBiographyChapter as jest.MockedFunction<typeof fetchBiographyChapter>;

const hotSpaceBody =
  'The bands 12th LP "Hot Space" is released in May while they are on an extensive tour of Europe. The album was different to other albums in that it had an emphasis on funky-disco type music. Fans from the very beginning were unsure as to what Queen were up to.\n\nBrian May still used his guitar, but synthesizers sat more forward than before.';

const hotSpaceSummary =
  'The bands 12th LP "Hot Space" is released in May while they are on an extensive tour of Europe. The album was different to other albums in that it had an emphasis on funky-disco type music. Fans from...';

function chapterFixture(overrides: Partial<BiographyChapterDetail> = {}): BiographyChapterDetail {
  return {
    id: 14,
    title: '1982',
    summary: hotSpaceSummary,
    body: hotSpaceBody,
    displaySequence: 14,
    detailPath: '/biography/14',
    previous: { id: 13, title: '1981', detailPath: '/biography/13' },
    next: { id: 15, title: '1983', detailPath: '/biography/15' },
    ...overrides,
  };
}

async function renderChapter(navigation = fakeNavigation(), id = 14) {
  return {
    navigation,
    ...(await renderWithProviders(
      <BiographyChapterScreen
        navigation={navigation as never}
        route={{ key: 'chapter', name: 'BiographyChapter', params: { id } } as never}
      />,
      { navigation: false },
    )),
  };
}

describe('BiographyChapterScreen', () => {
  beforeEach(() => {
    fetchChapter.mockReset();
    fetchChapter.mockResolvedValue(chapterFixture());
  });

  it('does not render a standfirst when summary is a truncated first paragraph of the body', async () => {
    await renderChapter();

    await waitFor(() => expect(screen.getByText('1982')).toBeOnTheScreen());
    expect(screen.getByText('Chapter 14')).toBeOnTheScreen();
    expect(screen.queryByText(hotSpaceSummary)).toBeNull();
    expect(
      screen.getAllByText(/The bands 12th LP "Hot Space" is released in May/),
    ).toHaveLength(1);
    expect(
      screen.getByText(/Fans from the very beginning were unsure as to what Queen were up to\./),
    ).toBeOnTheScreen();
  });

  it('does not render a standfirst for any chapter whose summary prefixes the body', async () => {
    fetchChapter.mockResolvedValue(
      chapterFixture({
        id: 1,
        title: '1970',
        displaySequence: 1,
        summary: 'Smile becomes Queen and the first album follows...',
        body: 'Smile becomes Queen and the first album follows in 1973. The lineup settles around Freddie, Brian, Roger and John.',
        previous: null,
        next: { id: 2, title: '1971', detailPath: '/biography/2' },
      }),
    );
    await renderChapter(fakeNavigation(), 1);

    await waitFor(() => expect(screen.getByText('1970')).toBeOnTheScreen());
    expect(screen.queryByText('Smile becomes Queen and the first album follows...')).toBeNull();
    expect(screen.getAllByText(/Smile becomes Queen and the first album follows/)).toHaveLength(1);
    expect(screen.getByText(/The lineup settles around Freddie, Brian, Roger and John\./)).toBeOnTheScreen();
  });
});
