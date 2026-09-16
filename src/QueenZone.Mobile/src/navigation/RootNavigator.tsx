import { getFocusedRouteNameFromRoute, type RouteProp } from '@react-navigation/native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { Archive, Camera, House, MessageSquare, Newspaper, type LucideIcon } from 'lucide-react-native';
import { useMemo } from 'react';
import { Platform, View, type ViewStyle } from 'react-native';
import { SignInScreen } from '../screens/account/SignInScreen';
import { testIds } from '../test/testIds';
import { useTheme } from '../theme';
import { EnvBanner } from '../ui/EnvBanner';
import { NotificationBridge } from '../notifications/NotificationBridge';
import { NewsShareBridge } from '../share/news/NewsShare';
import { WidgetLinkBridge } from '../widgets/WidgetLinkBridge';
import { SiriLinkBridge } from '../siri/SiriLinkBridge';
import { HeaderCloseButton } from './headerButtons';
import { ArchiveStack, ForumStack, HomeStack, NewsStack, PhotosStack, stackScreenOptions } from './stacks';
import type { RootStackParamList, RootTabParamList } from './types';
import { shouldHideTabBar } from './visibility';

const Tab = createBottomTabNavigator<RootTabParamList>();
const RootStack = createNativeStackNavigator<RootStackParamList>();

function TabGlyph({
  Icon,
  color,
  focused,
}: {
  Icon: LucideIcon;
  color: string;
  focused: boolean;
}) {
  const { c, chrome } = useTheme();
  const platform = Platform.OS === 'android' ? chrome.android : chrome.ios;
  const icon = <Icon color={color} size={platform.tabIcon} strokeWidth={1.5} />;

  if (platform.tabActiveStyle !== 'pill') {
    return icon;
  }

  return (
    <View
      style={{
        width: 64,
        height: 32,
        borderRadius: 16,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: focused ? c.accentTintWeak : 'transparent',
      }}
    >
      {icon}
    </View>
  );
}

function tabIcon(Icon: LucideIcon) {
  return function TabIcon({ color, focused }: { color: string; focused: boolean }) {
    return <TabGlyph Icon={Icon} color={color} focused={focused} />;
  };
}

const hiddenTabBarStyle = { display: 'none' } as const satisfies ViewStyle;

function tabBarStyleFor(visibleTabBarStyle: ViewStyle, hide: boolean) {
  return hide ? hiddenTabBarStyle : visibleTabBarStyle;
}

function hideTabBarIfDetail(
  visibleTabBarStyle: ViewStyle,
  route: RouteProp<RootTabParamList, keyof RootTabParamList>,
  initialRouteName: string,
) {
  const focused = getFocusedRouteNameFromRoute(route) ?? initialRouteName;
  return {
    tabBarStyle: tabBarStyleFor(visibleTabBarStyle, shouldHideTabBar(focused)),
  };
}

/** Archive and Forum use `{ always: true }` so a later tab press lands on
 * the tab root, not leftover Timeline / Category (#1387). */

export function reselectRoot(
  tabName: keyof RootTabParamList,
  screen: string,
  options?: { always?: boolean },
) {
  return ({
    navigation,
  }: {
    navigation: {
      isFocused: () => boolean;
      navigate: (name: keyof RootTabParamList, params: { screen: string }) => void;
    };
  }) => ({
    tabPress: (e: { preventDefault: () => void }) => {
      if (options?.always) {
        e.preventDefault();
        navigation.navigate(tabName, { screen });
        return;
      }
      if (navigation.isFocused()) {
        navigation.navigate(tabName, { screen });
      }
    },
  });
}

function MainTabs() {
  const { c, chrome } = useTheme();
  const platformChrome = Platform.OS === 'android' ? chrome.android : chrome.ios;
  const visibleTabBarStyle = useMemo(
    () => ({
      backgroundColor: c.surfacePage,
      borderTopColor: c.hairline,
      borderTopWidth: 1,
    }),
    [c.surfacePage, c.hairline],
  );

  return (
    <Tab.Navigator
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: c.accentPrimary,
        tabBarInactiveTintColor: c.textMuted,
        tabBarStyle: tabBarStyleFor(visibleTabBarStyle, false),
        tabBarLabelStyle: {
          fontSize: platformChrome.tabLabel,
          letterSpacing: 0.4,
        },
        tabBarHideOnKeyboard: true,
      }}
    >
      <Tab.Screen
        name="HomeTab"
        component={HomeStack}
        options={({ route }) => ({
          title: 'Home',
          tabBarAccessibilityLabel: 'Home',
          tabBarButtonTestID: testIds.tabHome,
          tabBarIcon: tabIcon(House),
          ...hideTabBarIfDetail(visibleTabBarStyle, route, 'Home'),
        })}
        listeners={reselectRoot('HomeTab', 'Home')}
      />
      <Tab.Screen
        name="NewsTab"
        component={NewsStack}
        options={({ route }) => ({
          title: 'News',
          tabBarAccessibilityLabel: 'News',
          tabBarButtonTestID: testIds.tabNews,
          tabBarIcon: tabIcon(Newspaper),
          ...hideTabBarIfDetail(visibleTabBarStyle, route, 'NewsIndex'),
        })}
        listeners={reselectRoot('NewsTab', 'NewsIndex')}
      />
      <Tab.Screen
        name="PhotosTab"
        component={PhotosStack}
        options={({ route }) => ({
          title: 'Photography',
          tabBarAccessibilityLabel: 'Photography',
          tabBarButtonTestID: testIds.tabPhotos,
          tabBarIcon: tabIcon(Camera),
          tabBarLabelStyle: {
            fontSize: platformChrome.tabLabel,
            letterSpacing: 0.2,
          },
          ...hideTabBarIfDetail(visibleTabBarStyle, route, 'PhotoIndex'),
        })}
        listeners={reselectRoot('PhotosTab', 'PhotoIndex')}
      />
      <Tab.Screen
        name="ArchiveTab"
        component={ArchiveStack}
        options={({ route }) => ({
          title: 'Archive',
          tabBarAccessibilityLabel: 'Archive',
          tabBarButtonTestID: testIds.tabArchive,
          tabBarIcon: tabIcon(Archive),
          ...hideTabBarIfDetail(visibleTabBarStyle, route, 'ArchiveHub'),
        })}
        listeners={reselectRoot('ArchiveTab', 'ArchiveHub', { always: true })}
      />
      <Tab.Screen
        name="ForumTab"
        component={ForumStack}
        options={({ route }) => ({
          title: 'Forum',
          tabBarAccessibilityLabel: 'Forum',
          tabBarButtonTestID: testIds.tabForum,
          tabBarIcon: tabIcon(MessageSquare),
          ...hideTabBarIfDetail(visibleTabBarStyle, route, 'ForumIndex'),
        })}
        listeners={reselectRoot('ForumTab', 'ForumIndex', { always: true })}
      />
    </Tab.Navigator>
  );
}

export function RootNavigator() {
  const { c } = useTheme();
  return (
    <View style={{ flex: 1 }}>
      <EnvBanner>
        <RootStack.Navigator screenOptions={{ headerShown: false }}>
          <RootStack.Screen name="Tabs" component={MainTabs} />
          <RootStack.Screen
            name="SignIn"
            component={SignInScreen}
            options={({ navigation }) => ({
              ...stackScreenOptions(c),
              headerShown: true,
              title: 'Sign in',
              presentation: 'fullScreenModal',
              headerLeft: () => (
                <HeaderCloseButton testID={testIds.signInClose} onPress={() => navigation.goBack()} />
              ),
            })}
          />
        </RootStack.Navigator>
      </EnvBanner>
      <NotificationBridge />
      <NewsShareBridge />
      <WidgetLinkBridge />
      <SiriLinkBridge />
    </View>
  );
}
