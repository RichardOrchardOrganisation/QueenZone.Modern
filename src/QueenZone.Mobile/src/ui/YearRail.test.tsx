import { fireEvent, screen } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { testIds } from '../test/testIds';
import { YearRail } from './YearRail';

async function layoutRail(height = 200) {
  await fireEvent(screen.getByTestId(testIds.newsYearRail), 'layout', {
    nativeEvent: { layout: { x: 0, y: 0, width: 28, height } },
  });
}

let touchTimeStamp = 0;

/** Minimal `TouchHistoryMath`-shaped touch history PanResponder needs to compute a centroid. */
function touchHistoryAt(pageY: number) {
  touchTimeStamp += 1;
  return {
    touchBank: [
      {
        touchActive: true,
        currentPageX: 0,
        currentPageY: pageY,
        previousPageX: 0,
        previousPageY: pageY,
        currentTimeStamp: touchTimeStamp,
      },
    ],
    numberActiveTouches: 1,
    indexOfSingleActiveTouch: 0,
    mostRecentTimeStamp: touchTimeStamp,
  };
}

async function grant(rail: ReturnType<typeof screen.getByTestId>, locationY: number) {
  await fireEvent(rail, 'responderGrant', { touchHistory: touchHistoryAt(locationY), nativeEvent: { locationY } });
}

async function move(rail: ReturnType<typeof screen.getByTestId>, locationY: number) {
  await fireEvent(rail, 'responderMove', { touchHistory: touchHistoryAt(locationY), nativeEvent: { locationY } });
}

async function release(rail: ReturnType<typeof screen.getByTestId>, locationY: number) {
  await fireEvent(rail, 'responderRelease', { nativeEvent: { locationY } });
}

describe('YearRail', () => {
  it('renders nothing when the archive spans fewer than two years', async () => {
    await renderWithProviders(<YearRail minYear={2026} maxYear={2026} activeYear={null} onSelectYear={jest.fn()} />);

    expect(screen.queryByTestId(testIds.newsYearRail)).not.toBeOnTheScreen();
  });

  it('renders nothing when the range has not loaded yet', async () => {
    await renderWithProviders(<YearRail minYear={null} maxYear={null} activeYear={null} onSelectYear={jest.fn()} />);

    expect(screen.queryByTestId(testIds.newsYearRail)).not.toBeOnTheScreen();
  });

  it('shows a floating year bubble while dragging and clears it on release', async () => {
    await renderWithProviders(<YearRail minYear={2006} maxYear={2026} activeYear={null} onSelectYear={jest.fn()} />);
    await layoutRail(200);
    const rail = screen.getByTestId(testIds.newsYearRail);

    await grant(rail, 0);
    expect(screen.getByText('2026')).toBeOnTheScreen();

    await move(rail, 200);
    expect(screen.getByText('2006')).toBeOnTheScreen();

    await release(rail, 200);
    expect(screen.queryByText('2006')).not.toBeOnTheScreen();
  });

  it('calls onSelectYear with the year under the touch point on release', async () => {
    const onSelectYear = jest.fn();
    await renderWithProviders(<YearRail minYear={2006} maxYear={2026} activeYear={null} onSelectYear={onSelectYear} />);
    await layoutRail(200);
    const rail = screen.getByTestId(testIds.newsYearRail);

    await grant(rail, 0);
    await release(rail, 0);

    expect(onSelectYear).toHaveBeenCalledWith(2026);
  });

  it('supports a discrete tap without any movement, for non-drag interaction', async () => {
    const onSelectYear = jest.fn();
    await renderWithProviders(<YearRail minYear={2006} maxYear={2026} activeYear={null} onSelectYear={onSelectYear} />);
    await layoutRail(200);
    const rail = screen.getByTestId(testIds.newsYearRail);

    await grant(rail, 100);
    await release(rail, 100);

    expect(onSelectYear).toHaveBeenCalledWith(2016);
  });

  it('clears the bubble on gesture termination without selecting a year', async () => {
    const onSelectYear = jest.fn();
    await renderWithProviders(<YearRail minYear={2006} maxYear={2026} activeYear={null} onSelectYear={onSelectYear} />);
    await layoutRail(200);
    const rail = screen.getByTestId(testIds.newsYearRail);

    await grant(rail, 100);
    await fireEvent(rail, 'responderTerminate', {});

    expect(screen.queryByText('2016')).not.toBeOnTheScreen();
    expect(onSelectYear).not.toHaveBeenCalled();
  });

  it('exposes an adjustable accessibility control that steps by one year', async () => {
    const onSelectYear = jest.fn();
    await renderWithProviders(<YearRail minYear={2006} maxYear={2026} activeYear={2020} onSelectYear={onSelectYear} />);
    const rail = screen.getByLabelText('Jump to year');

    expect(rail.props.accessibilityRole).toBe('adjustable');
    expect(rail.props.accessibilityValue).toEqual({ min: 2006, max: 2026, now: 2020, text: '2020' });

    await fireEvent(rail, 'accessibilityAction', { nativeEvent: { actionName: 'decrement' } });
    expect(onSelectYear).toHaveBeenCalledWith(2019);

    await fireEvent(rail, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });
    expect(onSelectYear).toHaveBeenCalledWith(2021);
  });
});
