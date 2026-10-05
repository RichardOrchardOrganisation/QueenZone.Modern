import type { ReactNode } from 'react';
import { memo } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { PhotoCategoryListItem } from '../../api';
import type { SectionView } from '../../hooks/useHomeSection';
import { ArchiveImage } from '../../ui/ArchiveImage';
import { MetaLine } from '../../ui/MetaLine';
import { SectionErrorBlock } from '../../ui/ScreenStates';
import { SectionHeader } from '../../ui/SectionHeader';
import { radius, space, type, useTheme } from '../../theme';
import { formatGalleryCardMeta } from './homeMeta';

export const HomeGallerySection = memo(function HomeGallerySection({
  galleryView,
  onOpenCategory,
  onBrowse,
  onReloadGallery,
}: {
  galleryView: SectionView<{ items: PhotoCategoryListItem[] }>;
  onOpenCategory: (category: PhotoCategoryListItem) => void;
  onBrowse: () => void;
  onReloadGallery: () => void;
}) {
  const { c } = useTheme();
  let galleryContent: ReactNode;
  if (galleryView.kind === 'skeleton') {
    galleryContent = <View style={styles.skeletonRow}>
      {[0, 1, 2].map((key) => (
        <View key={key} style={[styles.skeletonTile, { backgroundColor: c.surfaceCard }]} />
      ))}
    </View>;
  } else if (galleryView.kind === 'error') {
    galleryContent = <SectionErrorBlock message={galleryView.message} onRetry={onReloadGallery} />;
  } else {
    galleryContent = <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
      {galleryView.data.items.map((category) => (
        <Pressable
          key={category.catId}
          accessible
          accessibilityRole="button"
          accessibilityLabel={category.name}
          onPress={() => onOpenCategory(category)}
          style={styles.tile}
        >
          {category.coverThumbnailUrl ? (
            <ArchiveImage
              source={{ uri: category.coverThumbnailUrl }}
              label={category.name}
              priority="low"
              style={styles.thumb}
            />
          ) : (
            <View style={[styles.thumb, { backgroundColor: c.surfaceCard }]} />
          )}
          <Text style={[type.cardTitle, styles.tileTitle, { color: c.textPrimary }]}>{category.name}</Text>
          <MetaLine parts={[formatGalleryCardMeta(category)]} />
        </Pressable>
      ))}
    </ScrollView>;
  }

  return (
    <>
      <SectionHeader title="New in the gallery" actionLabel="Browse" onAction={onBrowse} />
      {galleryContent}
    </>
  );
});

const styles = StyleSheet.create({
  skeletonRow: { flexDirection: 'row', paddingHorizontal: space.xl, gap: 10 },
  skeletonTile: { width: space.card, height: space.card, borderRadius: radius.xs },
  scrollContent: { paddingHorizontal: space.xl, gap: 10 },
  tile: { width: space.card, gap: 9 },
  thumb: { width: space.card, height: space.card, borderRadius: radius.xs },
  tileTitle: { fontSize: 14 },
});
