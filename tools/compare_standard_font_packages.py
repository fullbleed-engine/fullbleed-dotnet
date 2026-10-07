#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Reject public 0.1.5's missing-font previews in an isolated Linux namespace."""
import argparse
from hashlib import sha256
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from urllib.request import urlopen
import zipfile

from verify_standard_fonts import verify

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.1.5'
PACKAGE_SHA256 = '9ef3696b303c82679e4cb0ad4f0ab2da9d0c10f7f73283fd443f49dd8d459eef'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def mask_system_fonts():
    assert sys.platform == 'linux' and os.geteuid() == 0, 'Requires Linux root inside a private mount namespace'
    assert os.readlink('/proc/self/ns/mnt') != os.readlink('/proc/1/ns/mnt'), 'Refusing to mask host fonts'
    subprocess.run(['mount', '--make-rprivate', '/'], check=True)
    empty = tempfile.TemporaryDirectory(prefix='fullbleed-dotnet-empty-fonts-')
    masked = []
    for path in [Path('/usr/share/fonts'), Path('/usr/local/share/fonts'),
                 Path.home() / '.fonts', Path.home() / '.local/share/fonts']:
        if path.is_dir():
            subprocess.run(['mount', '--bind', empty.name, str(path)], check=True)
            assert not any(path.iterdir()), path
            masked.append(str(path))
    os.environ.pop('FULLBLEED_FONT_DIR', None)
    return empty, masked


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path, help='Ordinary Linux consumer evidence to compare with font-hidden output')
    parser.add_argument('--packages', type=Path, default=ROOT / 'artifacts/packages')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/standard-font-comparison')
    parser.add_argument('--shell', default='pwsh')
    args = parser.parse_args()
    empty, masked = mask_system_fonts()
    try:
        output = args.output.resolve()
        output.mkdir(parents=True, exist_ok=False)
        packages = output / 'baseline-packages'
        packages.mkdir()
        with urlopen(URL, timeout=120) as response:
            data = response.read()
        assert sha256(data).hexdigest() == PACKAGE_SHA256, 'Previous public NuGet package changed'
        package = packages / f'FullBleed.DotNet.{VERSION}.nupkg'
        package.write_bytes(data)
        with zipfile.ZipFile(package) as archive:
            assert archive.testzip() is None
            native = json.loads(archive.read('native-provenance.json'))
            assert next(p for p in native['dependencies'] if p['name'] == 'fullbleed')['version'] == '2.5.10'
        (output / 'baseline-source.json').write_text(json.dumps(
            {'url': URL, 'sha256': PACKAGE_SHA256, 'engine': '2.5.10', 'bytes': len(data)}, indent=2) + '\n', encoding='utf-8')
        candidate = verify(args.candidate / 'standard-fonts')
        assert candidate['packageVersion'] != VERSION
        for label, version, source in [('baseline', VERSION, packages),
                                        ('candidate', candidate['packageVersion'], args.packages.resolve())]:
            command = [args.shell, '-NoProfile', '-File', str(ROOT / 'scripts/package-smoke.ps1'),
                       '-SkipPack', '-PackageVersion', version, '-PackageDirectory', str(source),
                       '-OutputDirectory', str(output / label)]
            with (output / (label + '-consumer.log')).open('w', encoding='utf-8') as log:
                subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        previous = verify(output / 'baseline/standard-fonts', allow_blank=True)
        current = verify(output / 'candidate/standard-fonts')
        assert previous['packageVersion'] == VERSION and current['packageVersion'] == candidate['packageVersion']
        assert previous['embeddedControlFontSha256'] == current['embeddedControlFontSha256'] == candidate['embeddedControlFontSha256']
        rejected = []
        for old, new, ordinary in zip(previous['cases'], current['cases'], candidate['cases'], strict=True):
            assert old['name'] == new['name'] == ordinary['name']
            for field in ['htmlSha256', 'cssSha256', 'pdfSha256', 'pdfiumPixelsSha256', 'text', 'faces']:
                assert old[field] == new[field] == ordinary[field], (new['name'], field)
            for field in ['previewSha256', 'htmlPreviewSha256']:
                assert new[field] == ordinary[field], (new['name'], 'System fonts affected candidate output', field)
            if old['font'] == 'Inter':
                assert old['nativeInkBounds'] and old['previewSha256'] == new['previewSha256']
                assert old['htmlPreviewSha256'] == new['htmlPreviewSha256']
            else:
                assert old['nativeInkBounds'] is None and new['nativeInkBounds'], new['name']
                if old['mode'] == 'pdf':
                    assert old['htmlInkBounds'] is None and new['htmlInkBounds']
                rejected.append(old['name'])
        assert len(rejected) == 48
        # The ordinary visibility gate must reject the inspected old package.
        try:
            verify(output / 'baseline/standard-fonts')
        except AssertionError as error:
            assert 'Blank native preview' in str(error), str(error)
        else:
            raise AssertionError('Old package unexpectedly passed the preview gate')
        report = {'ok': True, 'baselinePackage': VERSION, 'candidatePackage': current['packageVersion'],
                  'maskedFontDirectories': masked, 'cases': len(current['cases']), 'rejectedOldCases': rejected,
                  'pdfsUnchanged': True, 'embeddedControlsUnchanged': True, 'candidateMatchesOrdinaryHost': True,
                  'scope': 'Same packed NuGet consumers with system fonts hidden in a private Linux mount namespace. '
                           '48 unembedded Latin-font previews and 12 direct HTML previews are blank in public 0.1.5; '
                           'all candidate previews have ink and match the normal host. All 52 PDF byte sequences '
                           'and four embedded-font controls are unchanged. No host fonts are modified.'}
        (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(report))
    finally:
        empty.cleanup()


if __name__ == '__main__':
    main()
