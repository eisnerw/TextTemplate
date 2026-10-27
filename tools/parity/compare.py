#!/usr/bin/env python3
"""Build standalone C# and compare every unmodified reference template with Node."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

REPO = Path(__file__).resolve().parents[2]


def run(command):
    try:
        result = subprocess.run(command, cwd=REPO, env={**os.environ, 'TZ': 'UTC'},
                                capture_output=True, timeout=120)
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired as error:
        return 124, error.stdout or b'', b'Timed out after 120 seconds'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='Report and exact outputs directory (default: temporary directory)')
    args = parser.parse_args()
    output = (args.output or Path(tempfile.mkdtemp(prefix='texttemplate-parity-'))).resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='texttemplate-standalone-') as work:
        generated = Path(work) / 'generated'
        binary = Path(work) / 'bin'
        for command in [
            ['dotnet', 'run', '--project', 'src/TextTemplate.Compiler', '--', '--root', 'reference', '--output', str(generated)],
            ['dotnet', 'build', str(generated), '--nologo', '-o', str(binary), '-p:TemplateExtensionsDirectory=' + str(REPO / 'Extensions')],
        ]:
            code, stdout, stderr = run(command)
            if code:
                raise SystemExit((stdout + stderr).decode('utf-8', errors='replace'))
        rows = []
        for template in sorted((REPO / 'reference/templates').glob('*.template')):
            name = template.stem
            data = REPO / 'reference/data' / (name + '.json')
            common = ['--template', name]
            # Make the initial input explicit on both sides, including absent associated data.
            initial = data if data.exists() else Path(work) / 'empty.json'
            if not data.exists():
                initial.write_text('{}')
            common += ['--data-file', str(initial)]
            commands = [
                ['node', 'tools/node-reference/cli.mjs', *common, '--template-root', 'reference'],
                ['dotnet', str(binary / 'GeneratedTemplates.dll'), *common, '--data-root', 'reference/data'],
            ]
            results = [run(command) for command in commands]
            for label, (_, stdout, stderr) in zip(['node', 'csharp'], results):
                (output / (name + '.' + label + '.txt')).write_bytes(stdout)
                (output / (name + '.' + label + '.stderr')).write_bytes(stderr)
            a, b = results
            if a[0] and b[0] and all(b'Unsupported resource path: http' in r[2] for r in results):
                status = 'external-resource'
            elif a[0] or b[0]:
                status = 'execution-failure'
            else:
                status = 'match' if a[1] == b[1] else 'mismatch'
            row = dict(template=name, status=status, data=str(data.relative_to(REPO)) if data.exists() else {},
                       sourceSha256=hashlib.sha256(template.read_bytes()).hexdigest(),
                       nodeExit=a[0], csharpExit=b[0], nodeBytes=len(a[1]), csharpBytes=len(b[1]))
            if a[1] != b[1]:
                offset = next((i for i, (x, y) in enumerate(zip(a[1], b[1])) if x != y), min(len(a[1]), len(b[1])))
                row['firstMismatchByte'] = offset
                for label, result in zip(['node', 'csharp'], results):
                    row[label + 'Context'] = result[1][max(0, offset-30):offset+90].decode('utf-8', errors='backslashreplace')
            rows.append(row)
            print(f'{name:14} {status:18} {len(a[1]):7} / {len(b[1]):7} bytes', flush=True)
    provenance = REPO / 'tools/node-reference/.build/provenance.json'
    report = dict(timezone='UTC', templates=rows,
                  inputs={str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest()
                          for folder in ['reference/templates', 'reference/subtemplates', 'reference/data', 'reference/fixtures']
                          for p in sorted((REPO / folder).glob('*')) if p.is_file()})
    report['extensionSources'] = {str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest()
                                  for p in sorted((REPO / 'Extensions').rglob('*.cs'))}
    if provenance.exists():
        report['oracle'] = json.loads(provenance.read_text())
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Report:', output / 'report.json')
    return int(any(row['status'] != 'match' for row in rows))


if __name__ == '__main__':
    raise SystemExit(main())
