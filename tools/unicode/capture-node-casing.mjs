// Capture the reference Node runtime's casing tables. No Node dependency at deployment.
// Regeneration is deliberate: changing Node/Unicode versions changes template semantics.
import { writeFileSync } from 'node:fs';
if (process.versions.node !== '24.22.0' || process.versions.unicode !== '17.0')
  throw new Error('Use reference Node 24.22.0 / Unicode 17.0 to regenerate these tables.');
const upper = [], lower = [], cased = [], ignorable = [];
for (let code = 0; code <= 0x10ffff; code++) {
  if (code >= 0xd800 && code <= 0xdfff) continue;
  const text = String.fromCodePoint(code);
  if (text.toUpperCase() !== text) upper.push([code, text.toUpperCase()]);
  if (text.toLowerCase() !== text) lower.push([code, text.toLowerCase()]);
  if (/\p{Cased}/u.test(text)) cased.push(code);
  if (/\p{Case_Ignorable}/u.test(text)) ignorable.push(code);
}
const hex = n => '0x' + n.toString(16);
function mapping(name, values) {
  const ranges = [], expansions = [];
  for (const [code, text] of values) {
    if ([...text].length !== 1) { expansions.push([code, text]); continue; }
    const delta = text.codePointAt(0) - code, last = ranges.at(-1);
    if (last && last[1] + 1 === code && last[2] === delta) last[1] = code;
    else ranges.push([code, code, delta]);
  }
  const quoted = text => '"' + Array.from({length:text.length}, (_, i) => '\\u' + text.charCodeAt(i).toString(16).padStart(4, '0')).join('') + '"';
  return `    private static readonly int[] ${name} = {\n${ranges.map(([a,b,d]) => `        ${hex(a)}, ${hex(b)}, ${d},`).join('\n')}\n    };\n` +
    `    private static readonly Dictionary<int, string> ${name}Expansions = new()\n    {\n${expansions.map(([c,t]) => `        [${hex(c)}] = ${quoted(t)},`).join('\n')}\n    };\n`;
}
function properties(name, values) {
  const ranges = [];
  for (const code of values) {
    const last = ranges.at(-1);
    if (last && last[1] + 1 === code) last[1] = code;
    else ranges.push([code, code]);
  }
  return `    private static readonly int[] ${name} = {\n${ranges.map(([a,b]) => `        ${hex(a)}, ${hex(b)},`).join('\n')}\n    };\n`;
}
writeFileSync(new URL('../../src/TextTemplate.Compiler/Generation/StandaloneCasingData.cs.txt', import.meta.url),
  `// Captured by tools/unicode/capture-node-casing.mjs from Node ${process.versions.node}, Unicode ${process.versions.unicode}, ICU ${process.versions.icu}.\n` +
  '// Mapping results are frozen so deployed programs need neither Node nor host Unicode tables.\ninternal static partial class NodeCasing\n{\n' +
  mapping('Upper', upper) + mapping('Lower', lower) + properties('Cased', cased) + properties('CaseIgnorable', ignorable) + '}\n');
