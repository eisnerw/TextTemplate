import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));


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

for (const name of ['indexing', 'joining', 'dateforms', 'regexforms', 'dateparsing', 'regexnames', 'includescopes', 'replacevalues']) test(name + ' reference template and provenance', () => {
  const golden = JSON.parse(readFileSync(new URL('golden/' + name + '.json', import.meta.url)));
  for (const [file, hash] of Object.entries(golden.inputs))
    assert.equal(createHash('sha256').update(readFileSync(path.join(root, file))).digest('hex'), hash, file);
  assert.deepEqual(JSON.parse(readFileSync(path.join(root, 'tools/node-reference/.build/provenance.json'))), golden.oracle);
  assert.deepEqual(JSON.parse(run([...golden.args, '--report-json'])), golden.report);
  assert.equal(run(golden.args), readFileSync(new URL('collection-golden/' + name + '.txt', import.meta.url), 'utf8'));
});


// The interpreter throws with a machine-dependent stack here. Freeze the failure
// and its cause, not absolute build paths or an alleged rendered-output match.
for (const mode of ['html', 'xml']) test('date EncodeFor rejects ' + mode, () => {
  const output = run(['--template-text', "{[{compact.EncodeFor('" + mode + "')}].@DateTest(/^compact$/)}",
    '--data-file', 'reference/data/dateparsing.json'], 1);
  assert.ok(output.startsWith('EXCEPTION TypeError: str.replace is not a function'));
});
