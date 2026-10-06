#!/usr/bin/env node
/**
 * Classify an Android smoke/journeys failure as APP_CRASH vs not.
 *
 * A crash is our package's process gone (pidof empty on a live device) or a
 * logcat/tombstone Fatal signal / crash_dump / debuggerd report for
 * org.queenzone.mobile. Used by scripts/run-mobile-device-smoke.sh so a
 * native death is not reported as a Maestro selector miss (#2138).
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const ANDROID_APP_PACKAGE = 'org.queenzone.mobile';
/** Kernel comm is 15 chars; Android keeps the tail of the package name. */
export const ANDROID_APP_COMM = 'ueenzone.mobile';
export const APP_CRASH = 'APP_CRASH';
export const NO_CRASH = 'none';
export const MAX_FRAMES = 16;

const PACKAGE_RE = /org\.queenzone\.mobile|ueenzone\.mobile/;
const FATAL_SIGNAL_RE = /Fatal signal\s+(\d+)\s+\(([^)]+)\)(?:,\s*code\s+(\d+)\s+\(([^)]+)\))?/i;
const SIGNAL_RE = /(?:^|\s)signal\s+(\d+)\s+\(([^)]+)\)(?:,\s*code\s+(\d+)\s+\(([^)]+)\))?/i;
const CRASH_DUMP_RE = /crash_dump(?:32|64)?/i;
const TOMBSTONE_WRITTEN_RE = /Tombstone written to:\s*(\S+)/i;
const DEBUGGERD_HEADER_RE = /\*\*\* \*\*\* \*\*\*/;
const REPORT_START_RE = /Fatal signal [^\n]*|\*\*\* \*\*\* \*\*\*[^\n]*/g;
const FRAME_RE = /^\s*#\d+\s+pc\s+\S+/;
const THREADTIME_RE =
  /^\d{2}-\d{2} \d{2}:\d{2}:\d{2}[.,]\d+\s+\d+\s+\d+\s+[VDIWEF]\s+[^:]+:\s?(.*)$/;

export function stripLogcatPrefix(line) {
  const match = String(line ?? '').match(THREADTIME_RE);
  return match ? match[1] : String(line ?? '');
}

export function stripLogcatText(text) {
  return String(text ?? '')
    .split(/\r?\n/)
    .map(stripLogcatPrefix)
    .join('\n');
}

export function mentionsOurPackage(text) {
  return PACKAGE_RE.test(String(text ?? ''));
}

function formatSignal(number, name) {
  if (!number) {
    return null;
  }
  return name ? `${number} (${name})` : String(number);
}

function formatFaultCode(number, name) {
  if (!number) {
    return null;
  }
  return name ? `${number} (${name})` : String(number);
}

function parseSignal(report) {
  const fatal = FATAL_SIGNAL_RE.exec(report);
  if (fatal) {
    return {
      signal: formatSignal(fatal[1], fatal[2]),
      faultCode: formatFaultCode(fatal[3], fatal[4]),
    };
  }
  const signal = SIGNAL_RE.exec(report);
  if (signal) {
    return {
      signal: formatSignal(signal[1], signal[2]),
      faultCode: formatFaultCode(signal[3], signal[4]),
    };
  }
  return { signal: null, faultCode: null };
}

function parseFrames(report) {
  const frames = [];
  let inBacktrace = false;
  for (const line of String(report).split(/\r?\n/)) {
    if (/^\s*backtrace:\s*$/i.test(line)) {
      inBacktrace = true;
      continue;
    }
    if (inBacktrace) {
      if (FRAME_RE.test(line)) {
        frames.push(line.trim());
        if (frames.length >= MAX_FRAMES) {
          break;
        }
        continue;
      }
      if (line.trim() === '') {
        break;
      }
    }
    if (!inBacktrace && FRAME_RE.test(line)) {
      frames.push(line.trim());
      if (frames.length >= MAX_FRAMES) {
        break;
      }
    }
  }
  return frames;
}

function isCrashReport(report) {
  return (
    /Fatal signal/i.test(report) ||
    CRASH_DUMP_RE.test(report) ||
    TOMBSTONE_WRITTEN_RE.test(report) ||
    (DEBUGGERD_HEADER_RE.test(report) && /signal\s+\d+\s+\(/i.test(report))
  );
}

function parseReport(report, source) {
  const { signal, faultCode } = parseSignal(report);
  const tombstone = TOMBSTONE_WRITTEN_RE.exec(report)?.[1] ?? null;
  return {
    class: APP_CRASH,
    package: ANDROID_APP_PACKAGE,
    signal: signal ?? 'unknown',
    faultCode,
    frames: parseFrames(report),
    source,
    tombstonePath: tombstone,
  };
}

export function extractCrashReports(text) {
  const stripped = stripLogcatText(text);
  const starts = [];
  REPORT_START_RE.lastIndex = 0;
  let match = REPORT_START_RE.exec(stripped);
  while (match) {
    starts.push(match.index);
    match = REPORT_START_RE.exec(stripped);
  }
  if (starts.length === 0) {
    return stripped.trim() ? [stripped] : [];
  }
  return starts.map((start, index) => {
    const end = index + 1 < starts.length ? starts[index + 1] : stripped.length;
    return stripped.slice(start, end);
  });
}

export function parsePackageCrash(text, source = 'logcat') {
  const reports = extractCrashReports(text);
  let best = null;
  for (const report of reports) {
    if (!mentionsOurPackage(report) || !isCrashReport(report)) {
      continue;
    }
    const parsed = parseReport(report, source);
    if (!best) {
      best = parsed;
      continue;
    }
    best = {
      ...best,
      signal: parsed.signal !== 'unknown' ? parsed.signal : best.signal,
      faultCode: parsed.faultCode ?? best.faultCode,
      frames: parsed.frames.length >= best.frames.length ? parsed.frames : best.frames,
      tombstonePath: parsed.tombstonePath ?? best.tombstonePath,
      source,
    };
  }
  return best;
}

function readOptional(filePath) {
  if (!filePath || !existsSync(filePath)) {
    return '';
  }
  return readFileSync(filePath, 'utf8');
}

function parseProcessAlive(value) {
  if (value === true || value === 'true') {
    return true;
  }
  if (value === false || value === 'false') {
    return false;
  }
  return null;
}

export function classifyAndroidAppCrash({
  logcat = '',
  crashBuffer = '',
  tombstone = '',
  processAlive = null,
} = {}) {
  const alive = parseProcessAlive(processAlive);
  const sources = [
    [crashBuffer, 'crash-buffer'],
    [tombstone, 'tombstone'],
    [logcat, 'logcat'],
  ];
  for (const [text, source] of sources) {
    if (!text) {
      continue;
    }
    const parsed = parsePackageCrash(text, source);
    if (parsed) {
      return {
        ...parsed,
        processAlive: alive,
      };
    }
  }

  if (alive === false) {
    return {
      class: APP_CRASH,
      package: ANDROID_APP_PACKAGE,
      signal: 'process_gone',
      faultCode: null,
      frames: [],
      source: 'pidof',
      tombstonePath: null,
      processAlive: false,
    };
  }

  return {
    class: NO_CRASH,
    package: ANDROID_APP_PACKAGE,
    signal: null,
    faultCode: null,
    frames: [],
    source: null,
    tombstonePath: null,
    processAlive: alive,
  };
}

export function formatAppCrashReport(result) {
  if (!result || result.class !== APP_CRASH) {
    return '';
  }
  const lines = [
    `Maestro failing cause: ${APP_CRASH}`,
    `package=${result.package}`,
    `signal=${result.signal ?? 'unknown'}`,
  ];
  if (result.faultCode) {
    lines.push(`fault_code=${result.faultCode}`);
  }
  if (result.source) {
    lines.push(`source=${result.source}`);
  }
  if (result.processAlive === false) {
    lines.push('process_alive=false');
  }
  if (result.frames.length > 0) {
    lines.push('top native frames:');
    for (const frame of result.frames) {
      lines.push(`  ${frame}`);
    }
  }
  return lines.join('\n');
}

export function formatAppCrashSummary(result) {
  if (!result || result.class !== APP_CRASH) {
    return '';
  }
  const lines = [
    `## Android ${APP_CRASH}`,
    '',
    `- Package: \`${result.package}\``,
    `- Signal: ${result.signal ?? 'unknown'}`,
  ];
  if (result.faultCode) {
    lines.push(`- Fault code: ${result.faultCode}`);
  }
  if (result.source) {
    lines.push(`- Source: ${result.source}`);
  }
  if (result.processAlive === false) {
    lines.push('- Process: gone');
  }
  if (result.frames.length > 0) {
    lines.push('', '### Top native frames', '', '```');
    lines.push(...result.frames);
    lines.push('```');
  }
  return `${lines.join('\n')}\n`;
}

export function formatCrashMarker(result, { detectedAt = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z') } = {}) {
  const lines = [
    `class=${APP_CRASH}`,
    `package=${result.package}`,
    `signal=${result.signal ?? 'unknown'}`,
    `source=${result.source ?? 'unknown'}`,
    `detected_at=${detectedAt}`,
  ];
  if (result.faultCode) {
    lines.push(`fault_code=${result.faultCode}`);
  }
  if (result.processAlive === true || result.processAlive === false) {
    lines.push(`process_alive=${result.processAlive}`);
  }
  if (result.tombstonePath) {
    lines.push(`tombstone_path=${result.tombstonePath}`);
  }
  return `${lines.join('\n')}\n`;
}

export function writeCrashArtifacts(result, directory) {
  if (!result || result.class !== APP_CRASH || !directory) {
    return null;
  }
  const markerPath = path.join(directory, 'android-app-crash');
  const framesPath = path.join(directory, 'android-app-crash-frames.txt');
  const summaryPath = path.join(directory, 'android-app-crash-summary.md');
  writeFileSync(markerPath, formatCrashMarker(result));
  writeFileSync(framesPath, result.frames.length > 0 ? `${result.frames.join('\n')}\n` : '');
  writeFileSync(summaryPath, formatAppCrashSummary(result));
  return { markerPath, framesPath, summaryPath };
}

export function parseArgs(argv) {
  const args = {
    logcat: '',
    crashBuffer: '',
    tombstone: '',
    processAlive: null,
    writeDir: '',
    printReport: false,
    json: false,
  };
  for (let i = 0; i < argv.length; i += 1) {
    const flag = argv[i];
    const value = argv[i + 1];
    switch (flag) {
      case '--logcat':
        args.logcat = value ?? '';
        i += 1;
        break;
      case '--crash-buffer':
        args.crashBuffer = value ?? '';
        i += 1;
        break;
      case '--tombstone':
        args.tombstone = value ?? '';
        i += 1;
        break;
      case '--process-alive':
        args.processAlive = value ?? '';
        i += 1;
        break;
      case '--write-dir':
        args.writeDir = value ?? '';
        i += 1;
        break;
      case '--print-report':
        args.printReport = true;
        break;
      case '--json':
        args.json = true;
        break;
      default:
        throw new Error(`Unknown argument: ${flag}`);
    }
  }
  return args;
}

export function classifyFromFiles(args) {
  return classifyAndroidAppCrash({
    logcat: readOptional(args.logcat),
    crashBuffer: readOptional(args.crashBuffer),
    tombstone: readOptional(args.tombstone),
    processAlive: args.processAlive,
  });
}

export function main(argv = process.argv.slice(2), { stdout = console.log, stderr = console.error } = {}) {
  const args = parseArgs(argv);
  const result = classifyFromFiles(args);
  if (args.writeDir) {
    writeCrashArtifacts(result, args.writeDir);
  }
  if (args.json) {
    stdout(JSON.stringify(result, null, 2));
  } else if (args.printReport) {
    const report = formatAppCrashReport(result);
    if (report) {
      stderr(report);
    } else {
      stdout(`class=${result.class}`);
    }
  } else {
    stdout(`class=${result.class}`);
    if (result.signal) {
      stdout(`signal=${result.signal}`);
    }
  }
  return result;
}

const invokedDirectly =
  process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly) {
  main();
}
