import { parseArgs } from 'node:util';
import { readFile } from 'node:fs/promises';
import { Console } from 'node:console';
import { loadFixtures, resolveResource, deliverResource } from './resolver.mjs';

// Includes parser diagnostics emitted by the unchanged interpreter itself.
globalThis.console = new Console({ stdout: process.stderr, stderr: process.stderr });

const help = 'Usage: node tools/node-reference/cli.mjs (--template NAME | --template-file FILE | --template-text TEXT)\n'
  + '  [--data NAME | --data-file FILE] [--fixtures DIRECTORY] [--timeout-ms 10000]\n'
  + '  [--debug-level 0|1|2] [--status] [--report-json]\n'
  + 'Debug levels: 0 errors only (default), 1 warnings, 2 full console debug.\n'
  + 'Default fixtures: reference/fixtures (relative to the runner).\n'
  + 'Explicit data overrides associated data. stdout is exact rendered text.\n'
  + '--report-json instead emits the complete result and resource trace.\n'
  + 'Exit: 0 success, 1 interpreter diagnostics, 2 host/usage failure or timeout.\n';

try {
  const { values } = parseArgs({ options: {
    template: { type: 'string' }, 'template-file': { type: 'string' }, 'template-text': { type: 'string' },
    data: { type: 'string' }, 'data-file': { type: 'string' }, fixtures: { type: 'string' },
    'timeout-ms': { type: 'string' }, 'debug-level': { type: 'string' }, status: { type: 'boolean' },
    'report-json': { type: 'boolean' }, help: { type: 'boolean' },
  } });
  if (values.help) { process.stdout.write(help); } else {
    if (['template', 'template-file', 'template-text'].filter(k => values[k] !== undefined).length !== 1)
      throw new Error('Specify exactly one template input.\n' + help);
    if (values.data !== undefined && values['data-file'] !== undefined)
      throw new Error('Specify at most one data input.');
    const debugLevel = values['debug-level'] ?? '0';
    if (!['0', '1', '2'].includes(debugLevel))
      throw new Error('--debug-level must be 0 (errors), 1 (warnings), or 2 (debug)');
    if (Number(debugLevel) < 2) console.debug = () => {};
    if (Number(debugLevel) < 1) console.warn = () => {};
    // Keep console.log: the legacy lexer uses it for error diagnostics.
    const timeout = Number(values['timeout-ms'] ?? 10000);
    if (!Number.isSafeInteger(timeout) || timeout < 1 || timeout > 2147483647)
      throw new Error('--timeout-ms must be a positive integer <= 2147483647');
    const fixtures = await loadFixtures(values.fixtures || new URL('../../reference/fixtures/', import.meta.url));
    let input, data;
    let resourceFailure = false;
    if (values.template !== undefined) {
      const loaded = resolveResource(fixtures, '/template/' + encodeURIComponent(values.template));
      if (loaded?.error) throw new Error(loaded.error);
      if (typeof loaded === 'object') ({ template: input, data } = loaded);
      else input = loaded;
    } else input = values['template-file'] !== undefined
      ? await readFile(values['template-file'], 'utf8') : values['template-text'];
    if (values.data !== undefined) {
      data = resolveResource(fixtures, '/data/' + encodeURIComponent(values.data));
      if (data?.error) { resourceFailure = true; console.error(data.error); }
    }
    if (values['data-file'] !== undefined) data = JSON.parse(await readFile(values['data-file'], 'utf8'));
    if (typeof input !== 'string') throw new Error('Template resource is not text');
    const { interpret } = await import('./.build/src/main-generated/javascript/TextTemplateInterpreter.js');
    const events = [];
    const resources = [];
    let timer;
    let interpreterException = false;
    const result = await new Promise((resolve, reject) => {
      timer = setTimeout(() => reject(new Error('Evaluation did not complete within ' + timeout + ' ms')), timeout);
      const onEvent = event => {
        events.push(event.type);
        switch (event.type) {
          case 'status': if (values.status) console.error(event.status); break;
          case 'url': deliverResource(fixtures, event, reject, (path, data) => {
            resources.push({ path, data });
            if (data?.error) { resourceFailure = true; console.error(data.error); }
          }); break;
          case 'result': resolve(event); break;
          default: reject(new Error('Unknown callback type: ' + event.type));
        }
      };
      try { interpret(input, onEvent, { data }); } catch (error) {
        interpreterException = true;
        console.error(error.stack);
        onEvent({ type: 'result', result: 'EXCEPTION ' + error.stack, errors: [] });
      }
    }).finally(() => clearTimeout(timer));
    for (const error of result.errors || []) console.error(JSON.stringify(error));
    if (result.result != null && !['string', 'number', 'boolean'].includes(typeof result.result)) throw new Error('Non-text interpreter result: ' + JSON.stringify(result.result));
    process.stdout.write(values['report-json'] ? JSON.stringify({ ...result, events, resources }) : (result.result == null ? '' : String(result.result)));
    process.exitCode = resourceFailure || result.errors?.length || interpreterException ? 1 : 0;
  }
} catch (error) {
  console.error(error.stack || String(error));
  process.exitCode = 2;
}
