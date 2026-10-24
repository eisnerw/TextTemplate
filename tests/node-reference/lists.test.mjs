import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const repository = fileURLToPath(new URL('../../', import.meta.url));
const cases = ['list', 'regex', 'align', 'group', 'date', 'todate', 'encoding', 'order'].flatMap(suite => JSON.parse(readFileSync(new URL(suite + '-cases.json', import.meta.url))));
for (const item of cases) test('list characterization: ' + item.name, () => {
  const root = mkdtempSync(path.join(tmpdir(), 'texttemplate-list-'));
  try {
    mkdirSync(path.join(root, 'templates'));
    mkdirSync(path.join(root, 'subtemplates'));
    for (const [name, source] of Object.entries(item.shared ?? {}))
      writeFileSync(path.join(root, 'subtemplates', name + '.template'), source);
    writeFileSync(path.join(root, 'data.json'), JSON.stringify(item.data));
    const result = spawnSync(process.execPath, [path.join(repository, 'tools/node-reference/cli.mjs'),
      '--template-text', item.script, '--template-root', root, '--data-file', path.join(root, 'data.json')],
      { encoding: 'utf8', timeout: 15000, env: { ...process.env, TZ: item.timezone ?? 'UTC' } });
    assert.ifError(result.error);
    assert.equal(result.status, item.exitCode ?? 0, result.stderr);
    assert.equal(result.stdout, item.expected);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
