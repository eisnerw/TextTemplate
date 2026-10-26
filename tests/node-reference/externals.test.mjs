import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const golden = JSON.parse(readFileSync(new URL('golden/externals.json', import.meta.url)));
function run(args) {
  const result = spawnSync(process.execPath, ['tools/node-reference/cli.mjs', ...args], {
    cwd: root, encoding: 'utf8', timeout: 15000,
    env: { ...process.env, TZ: 'UTC', LANG: 'C.UTF-8' },
  });
  assert.ifError(result.error);
  assert.equal(result.signal, null, result.stderr);
  return result;
}

test('externals methods, value functions, composition and annotation scope', () => {
  for (const [file, hash] of Object.entries(golden.inputs))
    assert.equal(createHash('sha256').update(readFileSync(root + file)).digest('hex'), hash, file);
  const provenance = JSON.parse(readFileSync(root + 'tools/node-reference/.build/provenance.json'));
  assert.deepEqual(provenance, golden.oracle);
  const result = run([...golden.args, '--report-json']);
  assert.equal(result.status, golden.exitCode, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), golden.report);
  const raw = run(golden.args);
  assert.equal(raw.status, 0, raw.stderr);
  assert.equal(raw.stdout, golden.report.result);
  assert.equal(raw.stdout, readFileSync(new URL('collection-golden/externals.txt', import.meta.url), 'utf8'));
});

for (const [script, message] of [
  ["{name.test('<')}", 'Invalid parameters for test function'],
  ["{name.test('<', '>', '!')}", 'Invalid parameters for test function'],
  ["{[{name}].@ValueFunction('missing')}", 'Value Function not found: missing'],
]) test('externals diagnostic: ' + script, () => {
  const result = run(['--template-text', script, '--data-file', 'reference/data/externals.json', '--report-json']);
  assert.equal(result.status, 1, result.stderr);
  const report = JSON.parse(result.stdout);
  assert.equal(report.result, 'Alice');
  assert.deepEqual(report.errors.map(error => error.message), [message]);
});
