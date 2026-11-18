import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const RULE_RE = /^[a-z0-9]+(\.[a-z0-9-]+)+$/;
const LEVELS = new Set(['L1', 'L2', 'L3', 'L4', 'L5']);

export function repoRootFrom(moduleUrl = import.meta.url) {
  return path.resolve(path.dirname(fileURLToPath(moduleUrl)), '../..');
}

export function filerDir(root) {
  return path.join(root, '.github', 'issue-filer');
}

export function readJson(filePath) {
  return JSON.parse(readFileSync(filePath, 'utf8'));
}

export function areaForFile(filePath, areas = []) {
  const posix = String(filePath || '').replaceAll('\\', '/');
  for (const area of areas) {
    if ((area.prefixes || []).some((prefix) => posix.startsWith(prefix))) {
      return area.id;
    }
  }
  return 'unknown';
}

export function validateConfig(config) {
  const errors = [];
  if (!config || typeof config !== 'object' || Array.isArray(config)) {
    return ['config.json must be an object'];
  }
  const gardener = config.caps?.gardener;
  const telemetry = config.caps?.telemetry;
  if (!Number.isFinite(gardener?.maxIssues) || gardener.maxIssues < 1) {
    errors.push('caps.gardener.maxIssues must be a positive number');
  }
  if (!Number.isFinite(telemetry?.maxIssues) || telemetry.maxIssues < 1) {
    errors.push('caps.telemetry.maxIssues must be a positive number');
  }
  if (!config.labels?.gardener || !config.labels?.guardrail) {
    errors.push('labels.gardener and labels.guardrail are required');
  }
  if (!Array.isArray(config.areas)) {
    errors.push('areas must be an array');
  }
  if (!Array.isArray(config.suppressionPatterns)) {
    errors.push('suppressionPatterns must be an array');
  } else {
    for (const pattern of config.suppressionPatterns) {
      if (!pattern?.id || !pattern?.includes) {
        errors.push('each suppressionPatterns entry needs id and includes');
        break;
      }
    }
  }
  return errors;
}

export function isPositiveInteger(value) {
  return Number.isInteger(value) && value > 0;
}

function validateIgnoreCeiling(entry, index, field, errors) {
  if (entry[field] === undefined) {
    return;
  }
  if (!isPositiveInteger(entry[field])) {
    errors.push(`entries[${index}].${field} must be a positive integer`);
  }
}

export function validateIgnore(ignore) {
  const errors = [];
  if (!ignore || typeof ignore !== 'object' || Array.isArray(ignore)) {
    return ['ignore.json must be an object with an entries array'];
  }
  if (!Array.isArray(ignore.entries)) {
    return ['ignore.json must have an entries array'];
  }
  ignore.entries.forEach((entry, index) => {
    errors.push(...validateRecurrence(entry).map((error) => `entries[${index}].${error}`));
    if (!entry?.reason) {
      errors.push(`entries[${index}] is missing reason`);
    }
    if (!entry?.expires || Number.isNaN(Date.parse(entry.expires))) {
      errors.push(`entries[${index}] is missing a valid expires date`);
    }
    if (!entry?.match || typeof entry.match !== 'object') {
      errors.push(`entries[${index}] is missing match`);
    }
    validateIgnoreCeiling(entry, index, 'maxUsers', errors);
    validateIgnoreCeiling(entry, index, 'maxEvents', errors);
  });
  return errors;
}

export function validateRecurrence(entry) {
  if (entry?.recurrence === undefined) return [];
  const baseline = entry.recurrence;
  const errors = [];
  if (entry.match?.source !== 'sentry' || !/^sentry:\d+$/.test(entry.match?.key || '')) {
    errors.push('recurrence requires an exact Sentry source and issue key');
  }
  if (!/^[a-f0-9]{32}$/.test(baseline?.eventId || '')) {
    errors.push('recurrence.eventId must be a Sentry eventID');
  }
  if (typeof baseline?.lastSeen !== 'string' || !/^\d{4}-\d{2}-\d{2}T/.test(baseline.lastSeen)
      || !Number.isFinite(Date.parse(baseline.lastSeen))) {
    errors.push('recurrence.lastSeen must be an ISO timestamp');
  }
  if (!Number.isSafeInteger(baseline?.issueNumber) || baseline.issueNumber < 1) {
    errors.push('recurrence.issueNumber must be a positive issue number');
  }
  return errors;
}

export function validateFindingRules(rules) {
  if (!Array.isArray(rules)) {
    return ['finding-rules.json must be an array'];
  }
  const errors = [];
  rules.forEach((rule, index) => {
    if (!RULE_RE.test(rule?.id || '')) {
      errors.push(`finding-rules[${index}].id is not a valid rule id`);
    }
    if (!rule?.title) {
      errors.push(`finding-rules[${index}] is missing title`);
    }
    if (!LEVELS.has(rule?.level)) {
      errors.push(`finding-rules[${index}].level must be L1-L5`);
    }
    if (rule?.check != null && typeof rule.check !== 'string') {
      errors.push(`finding-rules[${index}].check must be a string or null`);
    }
  });
  return errors;
}

function nonemptyString(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function validChecklistDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}

export function validateReviewChecklist(checklist, findingRules, today = new Date().toISOString().slice(0, 10)) {
  if (!isObject(checklist) || checklist.version !== 1 || !Array.isArray(checklist.items)) {
    return ['review-checklist.json requires numeric version 1 and an items array'];
  }
  const errors = [];
  const ids = new Set();
  const rules = Array.isArray(findingRules) ? findingRules : [];
  checklist.items.forEach((item, index) => {
    const label = `review-checklist.items[${index}]`;
    if (!isObject(item)) {
      errors.push(`${label} must be an object`);
      return;
    }
    for (const field of ['id', 'check', 'owner']) {
      if (!nonemptyString(item[field])) errors.push(`${label}.${field} must be a nonempty string`);
    }
    if (ids.has(item.id)) errors.push(`${label}.id is a duplicate: ${item.id}`);
    ids.add(item.id);
    if (!Array.isArray(item.paths) || !item.paths.length || !item.paths.every(nonemptyString)) {
      errors.push(`${label}.paths must be a nonempty array of nonempty globs`);
    }
    if (!isObject(item.source) || !['incident', 'repeated-finding'].includes(item.source.type)
        || !nonemptyString(item.source.ref)) {
      errors.push(`${label}.source requires a valid type and nonempty ref`);
    }
    if (item.automatedCheck != null && typeof item.automatedCheck !== 'string') {
      errors.push(`${label}.automatedCheck must be a string or null`);
    }
    if (!validChecklistDate(item.expires)) {
      errors.push(`${label}.expires must be a UTC calendar date YYYY-MM-DD`);
    } else if (item.expires < today) {
      errors.push(`${label}.expires is expired: ${item.expires}`);
    }
    const repeated = item.source?.type === 'repeated-finding';
    if (repeated) {
      if (item.id !== item.source.ref) errors.push(`${label}.id must equal source.ref`);
      if (!rules.some((rule) => rule?.id === item.source.ref)) {
        errors.push(`${label}.source.ref is not a registered finding rule: ${item.source.ref}`);
      }
    }
    for (const rule of rules) {
      if (rule && (rule.id === item.id || (repeated && rule.id === item.source.ref))
          && (item.automatedCheck ?? null) !== (rule.check ?? null)) {
        errors.push(`${label}.automatedCheck disagrees with finding rule ${rule.id}.check`);
      }
    }
  });
  return errors;
}

export function loadFilerFiles(root) {
  const dir = filerDir(root);
  const config = readJson(path.join(dir, 'config.json'));
  const ignore = readJson(path.join(dir, 'ignore.json'));
  const findingRules = readJson(path.join(dir, 'finding-rules.json'));
  return { config, ignore, findingRules };
}

export function validateFilerFiles(root, today = new Date().toISOString().slice(0, 10)) {
  const loaded = loadFilerFiles(root);
  let checklistErrors;
  try {
    const checklist = readJson(path.join(root, 'config', 'review-checklist.json'));
    checklistErrors = validateReviewChecklist(checklist, loaded.findingRules, today);
  } catch (error) {
    checklistErrors = [`review-checklist.json could not be read: ${error.message}`];
  }
  return {
    ...loaded,
    errors: [
      ...validateConfig(loaded.config),
      ...validateIgnore(loaded.ignore),
      ...validateFindingRules(loaded.findingRules),
      ...checklistErrors,
    ],
  };
}

export function ruleInfo(ruleId, findingRules = []) {
  return findingRules.find((rule) => rule.id === ruleId) || null;
}
