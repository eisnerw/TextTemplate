import { readFile, readdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// A selected file root replaces both JSON template collections, with no fallback.
async function loadTemplateDirectory(directory) {
  const files = (await readdir(directory, { withFileTypes: true }))
    .filter(entry => entry.isFile() && entry.name.endsWith('.template'))
    .sort((a, b) => a.name.localeCompare(b.name));
  const entries = await Promise.all(files.map(async entry => [
    entry.name.slice(0, -'.template'.length),
    await readFile(path.join(directory, entry.name), 'utf8'),
  ]));
  return Object.fromEntries(entries);
}

export async function loadFixtures(directory, { templateRoot } = {}) {
  if (directory instanceof URL) directory = fileURLToPath(directory);
  if (templateRoot !== undefined) {
    const [templates, subtemplates, data] = await Promise.all([
      loadTemplateDirectory(path.join(templateRoot, 'templates')),
      loadTemplateDirectory(path.join(templateRoot, 'subtemplates')),
      readFile(path.join(directory, 'data.json'), 'utf8').then(JSON.parse),
    ]);
    return { templates, subtemplates, data };
  }
  const entries = await Promise.all(['templates', 'subtemplates', 'data'].map(async name =>
    [name, JSON.parse(await readFile(path.join(directory, name + '.json'), 'utf8'))]));
  return Object.fromEntries(entries);
}

// Deliberately retain the routes' truthiness checks and response shapes.
export function resolveResource(fixtures, resource) {
  const match = /^\/(template|subtemplate|data)\/([^/?#]+)$/.exec(resource);
  if (!match) throw new Error('Unsupported resource path: ' + resource);
  const [, kind, encoded] = match;
  const name = decodeURIComponent(encoded);
  const join = value => Array.isArray(value) ? value.join('\n') : value;
  if (kind === 'data') return fixtures.data[name] || { error: 'Data ' + name + ' not found' };
  if (kind === 'subtemplate') return fixtures.subtemplates[name]
    ? join(fixtures.subtemplates[name]) : { error: 'ERROR: Subtemplate #' + name + ' not found' };
  if (fixtures.templates[name]) {
    const template = join(fixtures.templates[name]);
    return fixtures.data[name] ? { template, data: fixtures.data[name] } : template;
  }
  if (name === 'data') return { template: 'first name:{firstName}', data: { firstName: 'Bill' } };
  return { error: 'ERROR: Template ' + name + ' not found' };
}

// ParserFacade's successful AJAX response normalization, including its truthiness.
export function browserResponse(data) {
  if (data.Result) return data.Result.replace(/\r/g, '');
  return data;
}

export function deliverResource(fixtures, event, onFailure, onResolved = () => {}) {
  setImmediate(() => {
    try {
      const data = browserResponse(resolveResource(fixtures, event.path));
      onResolved(event.path, data);
      event.success(data);
    } catch (error) { onFailure(error); }
  });
}
