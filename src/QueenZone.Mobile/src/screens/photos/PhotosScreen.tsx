import type { CompositeScreenProps } from '@react-navigation/native';
import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useCallback } from 'react';
import { FlatList, Pressable, Text, useWindowDimensions, View } from 'react-native';
import { fetchPhotoCategories, type PhotoCategoryListItem } from '../../api';
import { usePagedContent } from '../../hooks/usePagedContent';
import type { PhotosStackParamList, RootTabParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { openPhotoSubmit } from '../../session/signInNavigation';
import { radius, space, type, useTheme } from '../../theme';
import { ArchiveImage } from '../../ui/ArchiveImage';
import { Button } from '../../ui/Button';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { EmptyBlock, ErrorBlock, ListFooterLoading, LoadingBlock } from '../../ui/ScreenStates';
import { ThemedRefreshControl } from '../../ui/ThemedRefreshControl';
import { testIds } from '../../test/testIds';
import { photoCdnSource, photoCountLabel } from './photoGalleryMeta';

type Props = CompositeScreenProps<
  NativeStackScreenProps<PhotosStackParamList, 'PhotoIndex'>,
  BottomTabScreenProps<RootTabParamList>
>;

const GAP = 12;
const COLS = 2;

export function PhotosScreen({ navigation }: Props) {
  const { c } = useTheme();
  const { isSignedIn } = useSession();
  const width = useWindowDimensions().width;
  const tile = (width - space.xl * 2 - GAP) / COLS;
  const paged = usePagedContent<PhotoCategoryListItem>(
    useCallback((page, signal) => fetchPhotoCategories({ page, pageSize: 20, signal }), []),
    20,
  );

  if (paged.loading && paged.items.length === 0) {
    return (
      <View testID={testIds.photosScreen} style={{ flex: 1 }}>
        <LoadingBlock label="Loading photography…" />
      </View>
    );
  }

  if (paged.error && paged.items.length === 0) {
    return (
      <View testID={testIds.photosScreen} style={{ flex: 1 }}>
        <ErrorBlock message={paged.error} onRetry={paged.reload} />
      </View>
    );
  }

  return (
    <FlatList
      testID={testIds.photosScreen}
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      data={paged.items}
      keyExtractor={(item) => item.slug}
      numColumns={COLS}
      ListHeaderComponent={
        <PageTitleBlock
          eyebrow="The photographic archive"
          subtitle="Restored photographs, contact sheets and archive image collections, organised by collection."
        />
      }
      columnWrapperStyle={{ gap: GAP, paddingHorizontal: space.xl }}
      contentContainerStyle={{ paddingBottom: space.section }}
      ListEmptyComponent={<EmptyBlock message="No photo collections are available yet." />}
      alwaysBounceVertical
      ListFooterComponent={
        <View style={{ paddingTop: 26, alignItems: 'center', gap: space.md }}>
          <ListFooterLoading visible={paged.loadingMore} />
          {isSignedIn ? (
            <>
              <Button
                label="Submit a photo"
                variant="ghost"
                size="sm"
                onPress={() => navigation.navigate('PhotoSubmit')}
              />
              <Button
                label="My submissions"
                variant="ghost"
                size="sm"
                onPress={() => navigation.navigate('HomeTab', { screen: 'MySubmissions' })}
              />
            </>
          ) : (
            <Button
              label="Sign in to submit"
              variant="ghost"
              size="sm"
              onPress={() => openPhotoSubmit(navigation, isSignedIn, () => navigation.navigate('PhotoSubmit'))}
            />
          )}
        </View>
      }
      refreshControl={
        <ThemedRefreshControl refreshing={paged.refreshing} onRefresh={paged.refresh} />
      }
      onEndReached={paged.loadMore}
      onEndReachedThreshold={0.4}
      renderItem={({ item }) => {
        const cover = photoCdnSource(item.coverThumbnailUrl);
        return (
          <Pressable
            testID={`photo-category-${item.slug}`}
            accessibilityRole="button"
            accessibilityLabel={`${item.name}, ${photoCountLabel(item.imageCount)}`}
            onPress={() => navigation.navigate('PhotoCategory', { slug: item.slug, name: item.name })}
            style={{ width: tile, marginBottom: GAP }}
          >
            <View
              style={{
                height: tile * 0.78,
                borderRadius: radius.sm,
                overflow: 'hidden',
                backgroundColor: c.surfaceCard,
              }}
            >
              {cover ? (
                <ArchiveImage
                  source={cover}
                  label={item.name}
                  recyclingKey={item.slug}
                  priority="low"
                  style={{ width: '100%', height: '100%' }}
                />
              ) : null}
            </View>
            <Text
              style={[type.listTitle, { color: c.textPrimary, marginTop: space.sm }]}
              numberOfLines={2}
            >
              {item.name}
            </Text>
            <Text style={[type.meta, { color: c.textMuted, marginTop: 2 }]}>
              {photoCountLabel(item.imageCount)}
            </Text>
          </Pressable>
        );
      }}
    />
  );
}
