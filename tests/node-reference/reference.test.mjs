import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { loadFixtures, resolveResource, deliverResource, browserResponse } from '../../tools/node-reference/resolver.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const cli = path.join(root, 'tools/node-reference/cli.mjs');
const fixtures = await loadFixtures(path.join(root, 'reference/fixtures'));
const cases = JSON.parse(readFileSync(new URL('cases.json', import.meta.url)));
function run(args) {
  const child = spawnSync(process.execPath, [cli, ...args], {
    cwd: tmpdir(), encoding: 'utf8', timeout: 15000,
    env: { ...process.env, TZ: 'UTC', LANG: 'C.UTF-8' },
  });
  assert.ifError(child.error);
  assert.equal(child.signal, null, child.stderr);
  return child;
}
function exact(actual, expected, inputs) {
  if (actual === expected) return;
  let i = 0;
  while (i < Math.min(actual.length, expected.length) && actual[i] === expected[i]) i++;
  assert.fail(JSON.stringify({ inputs, firstMismatch: i,
    expected: expected.slice(Math.max(0, i - 35), i + 65),
    actual: actual.slice(Math.max(0, i - 35), i + 65) }));
}
for (const item of cases) test('legacy golden: ' + item.id, () => {
  const expected = JSON.parse(readFileSync(new URL('golden/' + item.id + '.json', import.meta.url)));
  const child = run([...item.args, '--report-json']);
  assert.equal(child.status, expected.exitCode, child.stderr);
  const report = JSON.parse(child.stdout);
  exact(JSON.stringify(report), JSON.stringify(expected.report), item);
  const raw = run(item.args);
  assert.equal(raw.status, expected.exitCode, raw.stderr);
  exact(raw.stdout, report.result == null ? '' : String(report.result), item);
});

test('legacy assertion successes and known failure remain explicit', () => {
  const read = name => JSON.parse(readFileSync(new URL('golden/' + name + '.json', import.meta.url))).report;
  assert.equal(read('legacy-tests').result, 'ALL TESTS PASSED');
  assert.match(read('legacy-doit-mismatch').result, /^ASSERT FAILURE:/);
  assert.equal(read('legacy-doit-mismatch').errors.length, 0);
  assert.equal(read('simple').result,
    'Hello Smith, Robert with a dog named Toto, a cat named Dolly and a zebra named Stripes!!');
});

test('route response shapes, newline joins, truthiness and missing entries', () => {
  const f = { templates: { a: ['a', 'b'], empty: '', zero: 0 }, subtemplates: { a: ['[a', 'b]'], empty: '' }, data: { a: { v: 1 }, zero: 0 } };
  assert.deepEqual(resolveResource(f, '/template/a'), { template: 'a\nb', data: { v: 1 } });
  assert.equal(resolveResource(f, '/subtemplate/a'), '[a\nb]');
  assert.deepEqual(resolveResource(f, '/data/a'), { v: 1 });
  assert.deepEqual(resolveResource(f, '/data/zero'), { error: 'Data zero not found' });
  assert.deepEqual(resolveResource(f, '/template/empty'), { error: 'ERROR: Template empty not found' });
  assert.deepEqual(resolveResource(f, '/subtemplate/empty'), { error: 'ERROR: Subtemplate #empty not found' });
  assert.deepEqual(resolveResource(f, '/template/data'), { template: 'first name:{firstName}', data: { firstName: 'Bill' } });
  assert.throws(() => resolveResource(f, 'https://example.invalid/data/a'), /Unsupported resource/);
  assert.equal(browserResponse({ Result: 'a\r\nb' }), 'a\nb');
  assert.deepEqual(browserResponse({ Result: null }), { Result: null });
  assert.deepEqual(browserResponse({ Result: '' }), { Result: '' });
});

test('resource continuation occurs after the caller returns', async () => {
  let returned = false;
  const completed = new Promise((resolve, reject) => deliverResource(fixtures, {
    path: '/data/nested',
    success(data) { try { assert.equal(returned, true); assert.equal(data.firstName, 'John'); resolve(); } catch (e) { reject(e); } },
  }, reject));
  returned = true;
  await completed;
});

test('browser shared definitions reproduce fetched result without further requests', () => {
  const fetched = JSON.parse(run(['--template', 'simpledata', '--report-json']).stdout);
  assert.deepEqual(fetched.resources.map(r => r.path), ['/subtemplate/doInclude', '/subtemplate/xformatPet']);
  const input = fixtures.templates.simpledata.join('\n') + fetched.resources.map(r =>
    '\n/*SHARED*/{#' + r.path.split('/')[2] + ':' + r.data + '}/*SHARED*/').join('');
  const child = run(['--template-text', input, '--data', 'simpledata', '--report-json']);
  assert.equal(child.status, 0, child.stderr);
  const shared = JSON.parse(child.stdout);
  exact(shared.result, fetched.result, { template: 'simpledata with browser shared definitions' });
  assert.deepEqual(shared.errors, []);
  assert.deepEqual(shared.resources, []);
});

test('file inputs and a custom fixture directory', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'texttemplate-reference-'));
  try {
    writeFileSync(path.join(dir, 'templates.json'), JSON.stringify({ hello: ['Hi {firstName}'] }));
    writeFileSync(path.join(dir, 'subtemplates.json'), '{}');
    writeFileSync(path.join(dir, 'data.json'), JSON.stringify({ hello: { firstName: 'Associated' } }));
    writeFileSync(path.join(dir, 'input.json'), JSON.stringify({ firstName: 'Override' }));
    writeFileSync(path.join(dir, 'input.txt'), 'Hi {firstName}');
    const named = run(['--template', 'hello', '--fixtures', dir]);
    assert.equal(named.status, 0, named.stderr);
    assert.equal(named.stdout, 'Hi Associated');
    const file = run(['--template-file', path.join(dir, 'input.txt'), '--data-file', path.join(dir, 'input.json')]);
    assert.equal(file.status, 0, file.stderr);
    assert.equal(file.stdout, 'Hi Override');
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test('falsy fetched result preserves legacy noncompletion and times out', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'texttemplate-reference-'));
  try {
    writeFileSync(path.join(dir, 'templates.json'), '{}');
    writeFileSync(path.join(dir, 'subtemplates.json'), JSON.stringify({ blank: [] }));
    writeFileSync(path.join(dir, 'data.json'), '{}');
    const child = run(['--template-text', '{#blank}', '--fixtures', dir, '--timeout-ms', '50']);
    assert.equal(child.status, 2);
    assert.equal(child.stdout, '');
    assert.match(child.stderr, /did not complete/);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test('host failures have no stdout and nonzero status', () => {
  for (const args of [[], ['--template', 'absent'], ['--template-text', "{'https://example.invalid/data':[{name}]}"]]) {
    const child = run(args);
    assert.equal(child.status, 2, child.stderr);
    assert.equal(child.stdout, '');
    assert.ok(child.stderr);
  }
});

test('status and interpreter console messages cannot contaminate stdout', () => {
  const child = run(['--template', 'simpledata', '--status', '--debug-level', '2']);
  assert.equal(child.status, 0);
  assert.match(child.stderr, /parsing\.\.\./);
  assert.match(child.stderr, /loading \/subtemplate\//);
  assert.match(child.stdout, /^Hello Smith, John/);
  assert.ok(!child.stdout.endsWith('\n'));
});

test('build and golden provenance match current preserved sources', () => {
  const expected = JSON.parse(readFileSync(new URL('golden/provenance.json', import.meta.url)));
  const built = JSON.parse(readFileSync(path.join(root, 'tools/node-reference/.build/provenance.json')));
  assert.deepEqual(built, expected);
  for (const [name, hash] of Object.entries(expected.sources)) {
    const source = name === 'TextTemplateInterpreter' ? path.join(root, 'reference/typescript/TextTemplateInterpreter.ts')
      : name === 'Externals' ? path.join(process.env.TEXTTEMPLATE_LEGACY_ROOT || path.join(root, '../texttemplate-editor'), 'src/main/typescript/Externals.ts')
      : path.join(root, name);
    assert.equal(createHash('sha256').update(readFileSync(source)).digest('hex'), hash, name);
  }
});

test('thrown interpreter exceptions retain worker output and fail', () => {
  const child = run(['--template-text', '{pets.OrderBy(name,1)}', '--data', 'simpledata', '--report-json']);
  assert.equal(child.status, 1);
  const report = JSON.parse(child.stdout);
  assert.match(report.result, /^EXCEPTION TypeError: argValues\[1\]\.toUpperCase is not a function\n/);
  assert.deepEqual(report.errors, []);
  assert.match(child.stderr, /TypeError/);
});

test('literal exception text is ordinary successful output', () => {
  const child = run(['--template-text', 'EXCEPTION literal text']);
  assert.equal(child.status, 0);
  assert.equal(child.stdout, 'EXCEPTION literal text');
});


test('debug levels control stderr without changing evaluation', () => {
  const args = ['--template-text', '{unknown}', '--report-json'];
  const quiet = run(args);
  assert.equal(quiet.status, 0);
  assert.equal(quiet.stderr, '');
  for (const level of ['0', '1', '2']) {
    const child = run([...args, '--debug-level', level]);
    assert.equal(child.status, quiet.status);
    assert.equal(child.stdout, quiet.stdout);
    if (level === '0') assert.equal(child.stderr, '');
    else assert.match(child.stderr, /without a data context/);
    if (level === '2') assert.match(child.stderr, /Missing value for unknown/);
    else assert.doesNotMatch(child.stderr, /Missing value for unknown/);
  }
  const error = run(['--template-text', '{', '--debug-level', '0']);
  assert.equal(error.status, 1);
  assert.match(error.stderr, /no viable alternative/);
  const status = run(['--template', 'simple', '--status', '--debug-level', '0']);
  assert.equal(status.status, 0);
  assert.match(status.stderr, /parsing/);
  for (const level of ['-1', '3', 'verbose', '']) {
    const invalid = run(['--template', 'simple', '--debug-level=' + level]);
    assert.equal(invalid.status, 2);
    assert.equal(invalid.stdout, '');
    assert.match(invalid.stderr, /--debug-level must be/);
  }
});


for (const item of cases) test('file collections match golden: ' + item.id, () => {
  const expected = JSON.parse(readFileSync(new URL('golden/' + item.id + '.json', import.meta.url)));
  const child = run([...item.args, '--template-root', path.join(root, 'reference'), '--report-json']);
  assert.equal(child.status, expected.exitCode, child.stderr);
  exact(child.stdout, JSON.stringify(expected.report), item);
});

test('startup is available by name or individual file in file mode', () => {
  const source = path.join(root, 'reference');
  const named = run(['--template', 'startup', '--template-root', source]);
  const file = run(['--template-file', path.join(source, 'templates/startup.template'), '--template-root', source]);
  assert.equal(named.status, 0, named.stderr);
  assert.equal(file.status, 0, file.stderr);
  assert.match(named.stdout, /Welcome to the TextTemplate documentation and editor/);
  assert.equal(named.stdout, file.stdout);
});

test('file mode uses sibling shared files and never falls back to JSON', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'texttemplate-files-'));
  try {
    mkdirSync(path.join(dir, 'templates'));
    mkdirSync(path.join(dir, 'subtemplates'));
    writeFileSync(path.join(dir, 'templates/simpledata.template'), '{#xformatPet}');
    writeFileSync(path.join(dir, 'subtemplates/xformatPet.template'), '[File says hello to {firstName}]');
    writeFileSync(path.join(dir, 'data.json'), JSON.stringify({ simpledata: { firstName: 'FileData' } }));
    // No templates.json/subtemplates.json: data still comes from --fixtures.
    const named = run(['--template', 'simpledata', '--template-root', dir, '--fixtures', dir]);
    assert.equal(named.status, 0, named.stderr);
    assert.equal(named.stdout, 'File says hello to FileData');
    const file = run(['--template-file', path.join(dir, 'templates/simpledata.template'), '--template-root', dir, '--data', 'nested']);
    assert.equal(file.status, 0, file.stderr);
    assert.equal(file.stdout, 'File says hello to John');
    // doInclude exists in the built-in JSON; it must not be found in this root.
    const missing = run(['--template-text', '{#doInclude}', '--template-root', dir]);
    assert.equal(missing.status, 1);
    assert.match(missing.stderr, /Subtemplate #doInclude not found/);
    const missingTemplate = run(['--template', 'simple', '--template-root', dir]);
    assert.equal(missingTemplate.status, 2);
    assert.match(missingTemplate.stderr, /Template simple not found/);
    const absentRoot = run(['--template', 'simple', '--template-root', path.join(dir, 'absent')]);
    assert.equal(absentRoot.status, 2);
    assert.match(absentRoot.stderr, /ENOENT/);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
