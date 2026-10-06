import { memo, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Gesture, GestureDetector } from 'react-native-gesture-handler';
import Animated, { useAnimatedStyle, useSharedValue } from 'react-native-reanimated';
import { testIds } from '../test/testIds';
import type { CrosswordDetail } from '../api/types';
import { useTheme } from '../theme';
import { runPhotoZoomOnJS } from '../screens/photos/photoZoomGestures';
import { cellLabel, type CrosswordModel, type CrosswordPlayState } from './core';
import { CrosswordBlock, CrosswordCell } from './CrosswordCell';

const MIN_ZOOM = 1;
const MAX_ZOOM = 4;

function clampCrosswordZoom(value: number) {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));
}

function crosswordCellLooks(model: CrosswordModel, state: CrosswordPlayState, puzzle: CrosswordDetail) {
  return {
    incorrectCells: new Set(state.incorrectCells),
    revealedCells: new Set(state.revealedCells),
    wordCells: new Set(model.entries[state.entry].cells),
    labels: puzzle.blocks.map((block, cell) => (block ? '' : cellLabel(model, state, cell))),
  };
}

type Props = { puzzle: CrosswordDetail; model: CrosswordModel; state: CrosswordPlayState; disabled: boolean; onCell: (cell: number) => void };

function CrosswordGridView({ puzzle, model, state, disabled, onCell }: Props) {
  const { c } = useTheme();
  const [viewport, setViewport] = useState({ width: 320, height: 320 });
  const [scale, setScale] = useState(MIN_ZOOM);
  const baseline = useSharedValue(MIN_ZOOM);
  const liveScale = useSharedValue(MIN_ZOOM);
  const horizontal = useRef<ScrollView>(null);
  const vertical = useRef<ScrollView>(null);
  const fitted = puzzle.width <= 9 ? Math.min(viewport.width, Math.max(100, viewport.height - 44)) : viewport.width;
  const boardHeight = Math.max(56, viewport.height - 44);
  const cellSize = Math.max(16, fitted / puzzle.width) * scale;
  const looks = useMemo(() => crosswordCellLooks(model, state, puzzle), [model, puzzle, state]);
  const gestures = useMemo(() => {
    const horizontalScroll = Gesture.Native();
    const verticalScroll = Gesture.Native();
    const pinch = runPhotoZoomOnJS(Gesture.Pinch())
      .onBegin(() => { baseline.set(liveScale.value); })
      .onUpdate(event => { liveScale.set(clampCrosswordZoom(baseline.value * event.scale)); })
      .onEnd(() => { const finalScale = clampCrosswordZoom(liveScale.value); liveScale.set(finalScale); setScale(finalScale); })
      .simultaneousWithExternalGesture(horizontalScroll, verticalScroll);
    horizontalScroll.simultaneousWithExternalGesture(pinch, verticalScroll);
    verticalScroll.simultaneousWithExternalGesture(pinch, horizontalScroll);
    return { pinch, horizontalScroll, verticalScroll };
  }, [baseline, liveScale]);
  useLayoutEffect(() => {
    liveScale.set(scale);
    baseline.set(scale);
  }, [baseline, liveScale, scale]);
  const animatedStyle = useAnimatedStyle(() => ({
    transform: [{ scale: liveScale.value / scale }],
    transformOrigin: 'top left',
  }));
  useEffect(() => {
    const x = (state.cell % puzzle.width) * cellSize;
    const y = Math.floor(state.cell / puzzle.width) * cellSize;
    horizontal.current?.scrollTo({ x: Math.max(0, x - viewport.width / 2 + cellSize / 2), animated: true });
    vertical.current?.scrollTo({ y: Math.max(0, y - boardHeight / 2 + cellSize / 2), animated: true });
  }, [state.cell, cellSize, puzzle.width, viewport, boardHeight]);
  return <View style={styles.frame} onLayout={event => setViewport(event.nativeEvent.layout)}>
    <GestureDetector gesture={gestures.pinch}>
      <View style={styles.scroll} collapsable={false}>
      <GestureDetector gesture={gestures.horizontalScroll}>
      <ScrollView ref={horizontal} horizontal nestedScrollEnabled bounces={false} style={styles.scroll} contentContainerStyle={{ minWidth: viewport.width, height: boardHeight }}>
        <GestureDetector gesture={gestures.verticalScroll}>
        <ScrollView ref={vertical} nestedScrollEnabled bounces={false} style={{ width: puzzle.width * cellSize, height: boardHeight }}>
          <Animated.View style={animatedStyle} collapsable={false}>
          <View testID={testIds.crosswordGrid} accessibilityLabel={`${puzzle.title} crossword grid. Pinch to zoom or use Zoom in.`}>
            {Array.from({ length: puzzle.height }, (_, row) => <View key={row} style={styles.row}>
              {Array.from({ length: puzzle.width }, (_, column) => {
                const cell = row * puzzle.width + column;
                if (puzzle.blocks[cell]) return <CrosswordBlock key={cell} size={cellSize} color={c.textPrimary} />;
                return <CrosswordCell key={cell} cell={cell} letter={state.letters[cell]} number={puzzle.numbering[cell] || ''}
                  size={cellSize} selected={state.cell === cell} inWord={looks.wordCells.has(cell)}
                  incorrect={looks.incorrectCells.has(cell)} revealed={looks.revealedCells.has(cell)}
                  disabled={disabled} label={looks.labels[cell] ?? ''} onPress={onCell} />;
              })}
            </View>)}
          </View>
          </Animated.View>
        </ScrollView>
        </GestureDetector>
      </ScrollView>
      </GestureDetector>
      </View>
    </GestureDetector>
    <View style={styles.zoom}>
      <Pressable accessibilityRole="button" accessibilityLabel="Zoom out" onPress={() => setScale(value => clampCrosswordZoom(value - 0.5))} style={styles.zoomButton}><Text style={{ color: c.accentPrimary }}>−</Text></Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel="Fit grid" onPress={() => setScale(MIN_ZOOM)} style={styles.zoomButton}><Text style={{ color: c.accentPrimary }}>Fit</Text></Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel="Zoom in" onPress={() => setScale(value => clampCrosswordZoom(value + 0.5))} style={styles.zoomButton}><Text style={{ color: c.accentPrimary }}>+</Text></Pressable>
    </View>
  </View>;
}

export const CrosswordGrid = memo(CrosswordGridView);

const styles = StyleSheet.create({ frame: { flex: 1, minHeight: 100, marginHorizontal: 12 }, scroll: { flex: 1 }, row: { flexDirection: 'row' },
  zoom: { flexDirection: 'row', justifyContent: 'center', height: 44 }, zoomButton: { minWidth: 44, height: 44, alignItems: 'center', justifyContent: 'center' } });
