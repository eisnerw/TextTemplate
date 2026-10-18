import { mkdir, readFile, writeFile, copyFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import ts from 'typescript';

const root = fileURLToPath(new URL('../../', import.meta.url));
const build = fileURLToPath(new URL('.build/', import.meta.url));
const legacy = path.resolve(process.env.TEXTTEMPLATE_LEGACY_ROOT || path.join(root, '../texttemplate-editor'));
const output = path.join(build, 'src/main-generated/javascript');
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const jar = path.join(build, 'antlr-4.9.3-complete.jar');
const jarHash = 'afcd40946d3de4d81e28d7c88d467289e0587285d27adb172aecc5494a17df36';
await mkdir(output, { recursive: true });
await mkdir(path.join(build, 'grammar'), { recursive: true });
try { await readFile(jar); } catch (error) {
  if (error.code !== 'ENOENT') throw error;
  const response = await fetch('https://www.antlr.org/download/antlr-4.9.3-complete.jar');
  if (!response.ok) throw new Error('ANTLR download: ' + response.status);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (sha256(bytes) !== jarHash) throw new Error('ANTLR download checksum mismatch');
  await writeFile(jar, bytes);
}
if (sha256(await readFile(jar)) !== jarHash) throw new Error('ANTLR checksum mismatch');
const sources = {};
for (const name of ['TextTemplateLexer.g4', 'TextTemplateParser.g4']) {
  const source = path.join(root, 'reference/grammar', name);
  sources['reference/grammar/' + name] = sha256(await readFile(source));
  await copyFile(source, path.join(build, 'grammar', name));
}
execFileSync(process.env.JAVA || 'java', ['-jar', jar, '-Dlanguage=JavaScript',
  '-no-listener', '-visitor', '-o', output, 'TextTemplateLexer.g4', 'TextTemplateParser.g4'],
{ cwd: path.join(build, 'grammar'), stdio: 'inherit' });
for (const [name, source] of [
  ['TextTemplateInterpreter', path.join(root, 'reference/typescript/TextTemplateInterpreter.ts')],
  ['Externals', path.join(legacy, 'src/main/typescript/Externals.ts')],
]) {
  const bytes = await readFile(source);
  sources[name] = sha256(bytes);
  const result = ts.transpileModule(bytes.toString('utf8'), {
    fileName: name + '.ts',
    compilerOptions: { target: ts.ScriptTarget.ES2016, module: ts.ModuleKind.ESNext },
    reportDiagnostics: true,
  });
  if (result.diagnostics?.length) throw new Error(ts.formatDiagnosticsWithColorAndContext(result.diagnostics, {
    getCanonicalFileName: f => f, getCurrentDirectory: () => root, getNewLine: () => '\n',
  }));
  await writeFile(path.join(output, name + '.js'), result.outputText);
}
for (const name of ['data', 'templates', 'subtemplates']) {
  sources['reference/fixtures/' + name + '.json'] = sha256(await readFile(path.join(root, 'reference/fixtures', name + '.json')));
}
await copyFile(path.join(legacy, 'LICENSE.txt'), path.join(build, 'LICENSE.legacy.txt'));
await writeFile(path.join(build, 'provenance.json'), JSON.stringify({
  sourceCommit: execFileSync('git', ['-C', legacy, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
  sources, antlrGenerator: '4.9.3', antlrRuntime: '4.11.0', moment: '2.29.4', typescript: ts.version,
}, null, 2) + '\n');
console.error('Built unchanged legacy interpreter and grammar in tools/node-reference/.build');
