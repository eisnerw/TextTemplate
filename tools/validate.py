#!/usr/bin/env python3
"""Regenerate standalone C#, run both test suites, and compare the collection."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts' / 'validation',
                        help='Directory for the collection comparison report')
    parser.add_argument('--allow-unavailable-resources', action='store_true',
                        help='Allow cases explicitly classified as external-resource; never allow output mismatches')
    args = parser.parse_args()
    output = args.output.resolve()
    stages = [
        ('Build Node reference', ['npm', 'run', 'build', '--prefix', 'tools/node-reference']),
        ('Generate C#', ['dotnet', 'run', '--project', 'src/TextTemplate.Compiler', '--', '--root', 'reference', '--output', 'Generated']),
        ('Build standalone C#', ['dotnet', 'build', 'Generated', '--nologo']),
        ('Node regressions', ['npm', 'test', '--prefix', 'tools/node-reference']),
        ('C# and A/B regressions', ['dotnet', 'test', 'tests/TextTemplate.Tests', '--nologo']),
    ]
    for name, command in stages:
        print('\n' + name, flush=True)
        result = subprocess.run(command, cwd=ROOT)
        if result.returncode:
            print(f'{name} failed (exit {result.returncode}).', file=sys.stderr)
            return result.returncode
    print('\nCompare reference collection', flush=True)
    (output / 'report.json').unlink(missing_ok=True)
    result = subprocess.run([sys.executable, 'tools/parity/compare.py', '--output', str(output)], cwd=ROOT)
    print('Comparison report: ' + str(output / 'report.json'), flush=True)
    if result.returncode == 0:
        return 0
    if result.returncode == 1 and args.allow_unavailable_resources:
        try:
            cases = json.loads((output / 'report.json').read_text())['templates']
            unavailable = [case['template'] for case in cases if case['status'] == 'external-resource']
            if cases and unavailable and all(case['status'] in ('match', 'external-resource') for case in cases):
                print('Local validation passed. Unavailable external cases: ' + ', '.join(unavailable))
                return 0
        except (OSError, KeyError, ValueError, TypeError):
            pass
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
