import { screen } from '@testing-library/react-native';
import { Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { renderWithProviders } from '../test/render';
import { testIds } from '../test/testIds';
import { EnvBanner, ENV_BANNER_HEIGHT } from './EnvBanner';

const mockAppConfig = {
  appEnv: 'development' as string,
  apiBaseUrl: 'http://qz.test',
  version: '0.1.0',
  smokeEmbed: true,
};

jest.mock('../config/appConfig', () => ({
  getAppConfig: () => mockAppConfig,
}));

function InsetProbe() {
  const insets = useSafeAreaInsets();
  return <Text>{`top-inset:${insets.top}`}</Text>;
}

describe('EnvBanner', () => {
  beforeEach(() => {
    mockAppConfig.appEnv = 'development';
    mockAppConfig.smokeEmbed = true;
  });

  it('shows LOCAL on development smoke embeds without covering children', async () => {
    await renderWithProviders(
      <EnvBanner>
        <View testID="child">
          <Text>home-profile</Text>
          <InsetProbe />
        </View>
      </EnvBanner>,
    );

    expect(screen.getByTestId(testIds.envBanner)).toBeOnTheScreen();
    expect(screen.getByText('LOCAL')).toBeOnTheScreen();
    expect(screen.getByTestId('child')).toBeOnTheScreen();
    expect(screen.getByText('home-profile')).toBeOnTheScreen();
    expect(screen.getByText('top-inset:0')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.envBanner)).toHaveStyle({ height: ENV_BANNER_HEIGHT });
  });

  it('shows DEV on staging', async () => {
    mockAppConfig.appEnv = 'staging';
    mockAppConfig.smokeEmbed = false;
    await renderWithProviders(
      <EnvBanner>
        <Text>child</Text>
      </EnvBanner>,
    );

    expect(screen.getByTestId(testIds.envBanner)).toBeOnTheScreen();
    expect(screen.getByText('DEV')).toBeOnTheScreen();
    expect(screen.queryByText('LOCAL')).toBeNull();
  });

  it('hides on production even when smokeEmbed is baked', async () => {
    mockAppConfig.appEnv = 'production';
    mockAppConfig.smokeEmbed = true;
    await renderWithProviders(
      <EnvBanner>
        <InsetProbe />
      </EnvBanner>,
    );

    expect(screen.queryByTestId(testIds.envBanner)).toBeNull();
    expect(screen.queryByText('LOCAL')).toBeNull();
    expect(screen.queryByText('DEV')).toBeNull();
    expect(screen.getByText('top-inset:47')).toBeOnTheScreen();
  });
});
