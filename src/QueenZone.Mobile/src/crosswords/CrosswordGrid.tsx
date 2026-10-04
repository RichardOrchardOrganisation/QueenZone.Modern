import { testIds } from '../test/testIds';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Gesture, GestureDetector } from 'react-native-gesture-handler';
import type { CrosswordDetail } from '../api/types';
import { fonts, useTheme } from '../theme';
import { cellLabel, type CrosswordModel, type CrosswordPlayState } from './core';

type Props = { puzzle: CrosswordDetail; model: CrosswordModel; state: CrosswordPlayState; disabled: boolean; onCell: (cell: number) => void };
export function CrosswordGrid({ puzzle, model, state, disabled, onCell }: Props) {
  const { c } = useTheme();
  const [viewport, setViewport] = useState({ width: 320, height: 320 });
  const [scale, setScale] = useState(1);
  const baseline = useRef(1);
  const currentScale = useRef(scale);
  currentScale.current = scale;
  const horizontal = useRef<ScrollView>(null);
  const vertical = useRef<ScrollView>(null);
  const fitted = puzzle.width <= 9 ? Math.min(viewport.width, Math.max(100, viewport.height - 44)) : viewport.width;
  const cellSize = Math.max(16, fitted / puzzle.width) * scale;
  const entry = model.entries[state.entry];
  const pinch = useMemo(() => Gesture.Simultaneous(Gesture.Native(), Gesture.Pinch().runOnJS(true)
    .onBegin(() => { baseline.current = currentScale.current; })
    .onUpdate(event => setScale(Math.min(4, Math.max(1, baseline.current * event.scale))))), []);
  useEffect(() => {
    const x = (state.cell % puzzle.width) * cellSize;
    const y = Math.floor(state.cell / puzzle.width) * cellSize;
    horizontal.current?.scrollTo({ x: Math.max(0, x - viewport.width / 2 + cellSize / 2), animated: true });
    vertical.current?.scrollTo({ y: Math.max(0, y - viewport.height / 2 + cellSize / 2), animated: true });
  }, [state.cell, cellSize, puzzle.width, viewport]);
  return <View style={styles.frame} onLayout={event => setViewport(event.nativeEvent.layout)}>
    <GestureDetector gesture={pinch}>
      <ScrollView ref={horizontal} horizontal bounces={false} style={styles.scroll} contentContainerStyle={{ minWidth: viewport.width }}>
        <ScrollView ref={vertical} bounces={false} style={{ width: puzzle.width * cellSize }}>
          <View testID={testIds.crosswordGrid} accessibilityLabel={`${puzzle.title} crossword grid. Pinch to zoom or use Zoom in.`}>
            {Array.from({ length: puzzle.height }, (_, row) => <View key={row} style={styles.row}>
              {Array.from({ length: puzzle.width }, (_, column) => {
                const cell = row * puzzle.width + column;
                if (puzzle.blocks[cell]) return <View key={cell} style={{ width: cellSize, height: cellSize, backgroundColor: c.textPrimary }} accessible={false} />;
                const selected = state.cell === cell;
                const incorrect = state.incorrectCells.includes(cell);
                const revealed = state.revealedCells.includes(cell);
                return <Pressable key={cell} testID={`crossword-cell-${cell}`} accessibilityRole="button"
                  accessibilityLabel={cellLabel(model, state, cell)} accessibilityState={{ selected, disabled }} disabled={disabled}
                  onPress={() => onCell(cell)} style={[styles.cell, { width: cellSize, height: cellSize,
                    backgroundColor: entry.cells.includes(cell) ? c.surfaceCard : c.surfacePage,
                    borderColor: selected ? c.accentPrimary : c.borderStrong, borderWidth: selected ? 3 : 0.5 }]}>
                  <Text maxFontSizeMultiplier={1.2} style={[styles.number, { color: c.textSecondary, fontSize: Math.max(8, cellSize * 0.22) }]}>{puzzle.numbering[cell] || ''}</Text>
                  <Text maxFontSizeMultiplier={1.2} style={{ fontFamily: fonts.bodyMedium, fontSize: cellSize * 0.5, color: c.textPrimary,
                    textDecorationLine: incorrect ? 'line-through' : 'none' }}>{state.letters[cell] === '.' ? '' : state.letters[cell]}</Text>
                  <Text accessibilityElementsHidden style={[styles.marker, { fontSize: Math.max(9, cellSize * 0.22), color: incorrect ? c.danger : c.accentPrimary }]}>{revealed ? '▲' : incorrect ? '×' : ''}</Text>
                </Pressable>;
              })}
            </View>)}
          </View>
        </ScrollView>
      </ScrollView>
    </GestureDetector>
    <View style={styles.zoom}>
      <Pressable accessibilityRole="button" accessibilityLabel="Zoom out" onPress={() => setScale(value => Math.max(1, value - 0.5))} style={styles.zoomButton}><Text style={{ color: c.accentPrimary }}>−</Text></Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel="Fit grid" onPress={() => setScale(1)} style={styles.zoomButton}><Text style={{ color: c.accentPrimary }}>Fit</Text></Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel="Zoom in" onPress={() => setScale(value => Math.min(4, value + 0.5))} style={styles.zoomButton}><Text style={{ color: c.accentPrimary }}>+</Text></Pressable>
    </View>
  </View>;
}
const styles = StyleSheet.create({ frame: { flex: 1, minHeight: 100, marginHorizontal: 12 }, scroll: { flex: 1 }, row: { flexDirection: 'row' },
  cell: { alignItems: 'center', justifyContent: 'center' }, number: { position: 'absolute', top: 0, left: 2, fontFamily: fonts.body },
  marker: { position: 'absolute', bottom: 0, right: 1 }, zoom: { flexDirection: 'row', justifyContent: 'center', height: 44 }, zoomButton: { minWidth: 44, height: 44, alignItems: 'center', justifyContent: 'center' } });
