#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Prove the inline-layout gate rejects public 0.1.4 and accepts the candidate."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from verify_inline_layout import MATRIX, MODES, verify

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.1.4'
PACKAGE_SHA256 = '543a709296e18f4ced53cfe154f2b526a8f4416c7cad266dc58b6572f56d5df6'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/inline-comparison')
    parser.add_argument('--shell', default='pwsh')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    packages = output / 'packages'
    packages.mkdir()
    with urlopen(URL, timeout=60) as response:
        data = response.read()
    assert sha256(data).hexdigest() == PACKAGE_SHA256, 'Previous public NuGet package changed'
    package = packages / f'FullBleed.DotNet.{VERSION}.nupkg'
    package.write_bytes(data)
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None
        native = json.loads(archive.read('native-provenance.json'))
        assert next(p for p in native['dependencies'] if p['name'] == 'fullbleed')['version'] == '2.5.8'
    (output / 'baseline-source.json').write_text(json.dumps(
        {'url': URL, 'sha256': PACKAGE_SHA256, 'engine': '2.5.8', 'bytes': len(data)}, indent=2) + '\n', encoding='utf-8')
    command = [args.shell, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'scripts/package-smoke.ps1'),
               '-SkipPack', '-PackageVersion', VERSION, '-PackageDirectory', str(packages),
               '-OutputDirectory', str(output / 'baseline')]
    with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
        subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    previous_root = output / 'baseline/inline-layout'
    try:
        verify(previous_root)
    except AssertionError:
        previous = json.loads((previous_root / 'verification.json').read_text(encoding='utf-8'))
    else:
        raise AssertionError('Previous package unexpectedly passed the inline-layout gate')
    healthy = {f'{mode}-{name}' for mode in MODES for name in ['plain', 'unstyled-span']}
    healthy.update(['sizing-flex-0', 'sizing-inline-block-0'])
    rejected = {case['name'] for case in previous['cases'] if not case['ok']}
    assert not previous['ok'] and previous['packageVersion'] == VERSION
    assert rejected == set(MATRIX) - healthy, 'Negative-control failures differ from the reviewed defects'
    current = verify(args.candidate / 'inline-layout')
    assert current['ok'] and current['packageVersion'] != VERSION
    before = {case['name']: case for case in previous['cases']}
    after = {case['name']: case for case in current['cases']}
    old_inputs = json.loads((previous_root / 'renders.json').read_text(encoding='utf-8'))
    new_inputs = json.loads((args.candidate / 'inline-layout/renders.json').read_text(encoding='utf-8'))
    assert old_inputs['fonts'] == new_inputs['fonts'], 'Before/after font inputs differ'
    for name in MATRIX:
        for key in ['htmlSha256', 'cssSha256']:
            assert before[name][key] == after[name][key], (name, 'before/after inputs differ', key)
    for name in healthy:
        for key in ['pypdfText', 'pdfiumText']:
            assert before[name][key] == after[name][key], (name, 'healthy control changed', key)
    reviewed = json.loads((ROOT / 'tests/FullBleed.DotNet.PackageSmoke/reviewed-layout-2.5.10.json').read_text(encoding='utf-8'))
    assert reviewed['package'] == current['packageVersion'] and reviewed['engine'] == '2.5.10'
    assert set(reviewed['inlineFixtures']) == {'sizing-flex-0', 'sizing-inline-block-0'}
    for name in healthy:
        if name in reviewed['inlineFixtures']:
            # Correct intrinsic advances also move the untracked labels slightly.
            # Allow only the separately inspected versioned PDF/preview baseline.
            for key, value in reviewed['inlineFixtures'][name].items():
                assert after[name][key] == value, (name, 'reviewed layout differs', key)
        else:
            for key in ['pdfSha256', 'previewSha256', 'pdfiumPixelsSha256']:
                assert before[name][key] == after[name][key], (name, 'healthy control changed', key)
    report = {'ok': True, 'baselinePackage': VERSION, 'candidatePackage': current['packageVersion'],
              'cases': len(MATRIX), 'rejectedOldCases': sorted(rejected),
              'healthyTextControls': sorted(healthy),
              'unchangedWrappingControls': sorted(healthy - set(reviewed['inlineFixtures'])),
              'reviewedSizingChanges': sorted(reviewed['inlineFixtures']),
              'scope': 'The same 54 HTML/CSS fixtures and fonts rendered through isolated NuGet consumers. '
                       'Public 0.1.4 fails 40 styled wrapping and four tracked-label cases; all candidate cases pass. '
                       'Ten healthy controls retain text; eight wrapping controls retain PDF bytes and preview pixels. '
                       'Two untracked sizing controls must match separately reviewed versioned PDF/preview hashes. '
                       'No universal layout, conformance, or speed claim.'}
    (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
