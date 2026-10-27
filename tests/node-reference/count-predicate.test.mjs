import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const golden = JSON.parse(readFileSync(new URL('golden/countpredicate.json', import.meta.url)));
const cases = JSON.parse(readFileSync(new URL('count-predicate-cases.json', import.meta.url)));
function run(args, expectedExit = 0) {
  const result = spawnSync(process.execPath, ['tools/node-reference/cli.mjs', ...args], {
    cwd: root, encoding: 'utf8', timeout: 15000,
    env: { ...process.env, TZ: 'UTC', LANG: 'C.UTF-8' },
  });
  assert.ifError(result.error);
  assert.equal(result.signal, null, result.stderr);
  assert.equal(result.status, expectedExit, result.stderr);
  return result.stdout;
}

test('count predicate reference template and provenance', () => {
  for (const [file, hash] of Object.entries(golden.inputs))
    assert.equal(createHash('sha256').update(readFileSync(path.join(root, file))).digest('hex'), hash, file);
  assert.deepEqual(JSON.parse(readFileSync(path.join(root, 'tools/node-reference/.build/provenance.json'))), golden.oracle);
  assert.deepEqual(JSON.parse(run([...golden.args, '--report-json'])), golden.report);
  assert.equal(run(golden.args), readFileSync(new URL('collection-golden/countpredicate.txt', import.meta.url), 'utf8'));
});

for (const item of cases) test('count predicate: ' + item.name, () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'texttemplate-count-'));
  try {
    const data = path.join(directory, 'data.json');
    writeFileSync(data, JSON.stringify(item.data));
    assert.equal(run(['--template-text', item.script, '--data-file', data], item.exitCode ?? 0), item.expected, JSON.stringify(item));
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
