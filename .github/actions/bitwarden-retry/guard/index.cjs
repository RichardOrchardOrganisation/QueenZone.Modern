const fs = require('node:fs');
const crypto = require('node:crypto');

class FetchValidationError extends Error {}
// Env-var / Actions output names, including conventional TF_VAR_<lowercase>
// aliases used by OpenTofu. COMPLETE is reserved by the official action and
// this guard's own complete marker.
function isAllowedSecretName(name) {
  return /^[A-Za-z_][A-Za-z0-9_]*$/.test(name) && name.toUpperCase() !== 'COMPLETE';
}
function parseMapping(text) {
  const entries = String(text || '').split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const ids = new Set(); const names = new Set();
  if (!entries.length) throw new FetchValidationError('Secret mapping is empty.');
  for (const line of entries) {
    const match = /^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\s*>\s*([A-Za-z_][A-Za-z0-9_]*)$/i.exec(line);
    if (!match || !isAllowedSecretName(match[2])) {
      throw new FetchValidationError('Secret mapping must contain UUID > env-var name entries.');
    }
    const id = match[1].toLowerCase(); const name = match[2];
    if (ids.has(id) || names.has(name)) throw new FetchValidationError('Secret mapping has duplicate IDs or output names.');
    ids.add(id); names.add(name);
  }
  return [...names];
}
function validateOutputs(names, data) {
  let outputs;
  try { outputs = JSON.parse(data); } catch { throw new FetchValidationError('Fetch outputs are invalid.'); }
  if (!outputs || Array.isArray(outputs) || typeof outputs !== 'object'
      || Object.keys(outputs).length !== names.length
      || names.some((name) => !Object.hasOwn(outputs, name) || typeof outputs[name] !== 'string')) {
    throw new FetchValidationError('Fetch did not return the complete requested secret mapping.');
  }
  return outputs;
}
function fileCommand(name, value) {
  let delimiter;
  do { delimiter = `qz_${crypto.randomUUID()}`; } while (value.includes(delimiter));
  return `${name}<<${delimiter}\n${value}\n${delimiter}\n`;
}
function execute({ phase, mapping, data, outputPath, append = fs.appendFileSync, mask = (value) => {
  console.log(`::add-mask::${value.replaceAll('%', '%25').replaceAll('\r', '%0D').replaceAll('\n', '%0A')}`);
} }) {
  const names = parseMapping(mapping);
  if (phase === 'preflight') return;
  const outputs = validateOutputs(names, data);
  if (phase === 'check') { append(outputPath, 'complete=true\n'); return; }
  if (phase !== 'publish') throw new FetchValidationError('Unknown fetch validation phase.');
  // Validate the entire response and mask everything before writing anything.
  for (const value of Object.values(outputs)) mask(value);
  const payload = names.map((name) => fileCommand(name, outputs[name])).join('') + 'complete=true\n';
  // One bounded publication. A write failure fails this step; composite outputs
  // additionally require publish.outcome == success, even if this file is partial.
  append(outputPath, payload);
}
if (require.main === module) {
  try {
    const attempt = Number(process.env.INPUT_ATTEMPT || 1);
    if (![1, 2, 3].includes(attempt)) throw new FetchValidationError('Invalid fetch attempt metadata.');
    execute({ phase: process.env.INPUT_PHASE, mapping: process.env.INPUT_MAPPING,
      data: process.env.FETCH_OUTPUTS || '{}', outputPath: process.env.GITHUB_OUTPUT });
    if (process.env.INPUT_PHASE === 'publish') console.log(`Complete Bitwarden fetch accepted on attempt ${attempt} of 3.`);
  } catch (error) {
    console.error(error instanceof FetchValidationError ? error.message : 'Secret output publication failed.');
    process.exitCode = 1;
  }
}
module.exports = { parseMapping, validateOutputs, execute, isAllowedSecretName };
