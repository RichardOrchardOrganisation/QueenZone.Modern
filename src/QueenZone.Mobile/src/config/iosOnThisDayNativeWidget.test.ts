import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const {
  applyOnThisDayNativeWidget,
  copyWidgetCrestAsset,
  CREST_ASSET_NAME,
  GENERATED_ENTRY,
  NATIVE_ENTRY,
  TAG,
} = require('../../plugins/withIosOnThisDayNativeWidget.cjs') as {
  applyOnThisDayNativeWidget: (contents: string) => string;
  copyWidgetCrestAsset: (projectRoot: string, platformProjectRoot: string) => string;
  CREST_ASSET_NAME: string;
  GENERATED_ENTRY: string;
  NATIVE_ENTRY: string;
  TAG: string;
};

const generatedSwift = `import WidgetKit
import SwiftUI
internal import ExpoWidgets

struct OnThisDayWidget: Widget {
  let name: String = "OnThisDayWidget"

  var body: some WidgetConfiguration {
    StaticConfiguration(kind: name, provider: WidgetsTimelineProvider(name: name)) { entry in
      WidgetsEntryView(entry: entry)
    }
    .configurationDisplayName("On This Day")
    .description("A Queen history highlight and a rolling random quote.")
    .supportedFamilies([.systemSmall, .systemMedium])
  }
}
`;

describe('applyOnThisDayNativeWidget', () => {
  it('replaces the JSC entry view with native SwiftUI once', () => {
    const first = applyOnThisDayNativeWidget(generatedSwift);
    assert.equal(first.includes(GENERATED_ENTRY), false);
    assert.equal(first.includes(NATIVE_ENTRY), true);
    assert.match(first, /struct OnThisDayNativeEntryView/);
    assert.match(first, /Open QueenZone to load today's story\./);
    assert.match(first, /queenzone:\/\/home/);
    assert.equal(first.includes('queenzone://quotes/\\(quoteId)'), true);
    assert.equal(first.includes('queenzone://timeline/\\(eventId)'), true);
    assert.match(first, /queenzone:\/\/trivia/);
    assert.match(first, /queenzone:\/\/timeline"/);
    assert.match(first, /intProp\("quoteId"\)/);
    assert.match(first, /intProp\("eventId"\)/);
    assert.match(first, /containerBackground/);
    assert.match(first, /foregroundColor/);
    assert.match(first, /QUEEN QUOTES/);
    assert.match(first, /QUEEN FACTS/);
    assert.match(first, /stringProp\("triviaText"\)/);
    assert.match(first, /faces\.append\("trivia"\)/);
    assert.match(first, /4 \* 3600/);
    assert.match(first, /crest-widget-watermark/);
    assert.match(first, /ZStack/);
    assert.match(first, /^import UIKit$/m);
    assert.equal(first.includes('@Environment(\\.widgetFamily)'), true);
    assert.match(first, /family == \.systemMedium \? 22 : 17/);
    assert.match(first, /family == \.systemMedium \? 11 : 9/);
    assert.match(first, /minimumScaleFactor\(0\.65\)/);
    assert.match(first, /\.lineLimit\(6\)/);
    assert.match(first, /\.lineLimit\(2\)/);
    assert.match(first, /frame\(maxHeight: \.infinity, alignment: \.topLeading\)/);
    assert.match(first, /Text\(summary\)/);
    assert.match(first, /Text\(formattedDate\)/);
    assert.equal(first.includes('Text("“\\(quoteText)”")'), true);
    assert.equal(first.includes('Text("— \\(quoteWhoSaid)")'), true);
    assert.match(first, /Text\(triviaText\)/);
    assert.equal(first.includes('queenzone://trivia/\\(triviaId)'), true);
    assert.match(first, /intProp\("triviaId"\)/);
    assert.equal(first.includes('.lineLimit(3)'), false);
    assert.equal(first.includes('formattedDate): \\(summary)'), false);
    assert.equal(first.includes('” — \\(quoteWhoSaid)'), false);
    assert.equal(first.includes(TAG), true);

    const second = applyOnThisDayNativeWidget(first);
    assert.equal(second, first);
  });

  it('fails closed when the expo-widgets template no longer uses WidgetsEntryView', () => {
    assert.throws(
      () => applyOnThisDayNativeWidget('struct OnThisDayWidget: Widget {}'),
      /template may have changed/,
    );
  });
});

describe('copyWidgetCrestAsset', () => {
  it('copies the faint crest into the widget target folder', () => {
    const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
    const destRoot = mkdtempSync(path.join(tmpdir(), 'qz-widget-crest-'));
    try {
      const dest = copyWidgetCrestAsset(projectRoot, destRoot);
      assert.equal(path.basename(dest), CREST_ASSET_NAME);
      assert.equal(existsSync(dest), true);
    } finally {
      rmSync(destRoot, { recursive: true, force: true });
    }
  });
});
