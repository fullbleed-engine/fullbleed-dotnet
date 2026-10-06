"""Compare current consumer output with the pinned previous public NuGet package."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
from urllib.request import urlopen
import zipfile

from verify_font_outputs import ROOT, verify
from verify_font_families import verify as verify_families

VERSION = '0.1.3'
PACKAGE_SHA256 = 'a1e2fa2efa3cb8c76999fedab891ce656146a9f98a8861d113ff58ed9639e44d'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/font-comparison')
    parser.add_argument('--shell', default='pwsh')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    packages = output / 'packages'
    packages.mkdir()
    with urlopen(URL, timeout=120) as response:
        data = response.read()
    assert hashlib.sha256(data).hexdigest() == PACKAGE_SHA256, 'Previous public package changed'
    package = packages / f'FullBleed.DotNet.{VERSION}.nupkg'
    package.write_bytes(data)
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None
        provenance = json.loads(archive.read('native-provenance.json'))
        assert next(item for item in provenance['dependencies'] if item['name'] == 'fullbleed')['version'] == '2.5.7'
    (output / 'baseline-source.json').write_text(json.dumps(
        {'url': URL, 'sha256': PACKAGE_SHA256, 'bytes': len(data), 'engine': '2.5.7'}, indent=2) + '\n', encoding='utf-8')
    baseline = output / 'baseline'
    command = [args.shell, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'scripts/package-smoke.ps1'),
               '-SkipPack', '-PackageVersion', VERSION, '-PackageDirectory', str(packages),
               '-OutputDirectory', str(baseline)]
    with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
        subprocess.run(command, cwd=ROOT, check=True, stdout=log, stderr=subprocess.STDOUT)
    previous = verify(baseline, legacy_family=True)
    assert previous['packageVersion'] == VERSION
    try:
        verify_families(baseline / 'font-families')
    except AssertionError as error:
        assert str(error).startswith('italic-first-normal-pdf: expected DMSerifDisplay-Regular'), str(error)
        (output / 'negative-control.txt').write_text(
            'Expected rejection of the old family default: ' + str(error) + '\n', encoding='utf-8')
    else:
        raise AssertionError('The previous package unexpectedly passed the family selection gate')
    candidate = verify(args.candidate)
    assert candidate['packageVersion'] != VERSION
    families = verify_families(args.candidate / 'font-families')
    assert len(families['cases']) == 22
    before = {item['name']: item for item in previous['fixtures']}
    after = {item['name']: item for item in candidate['fixtures']}
    assert set(before) == set(after)
    assert previous['inputs'] == candidate['inputs'], 'Before/after fixture inputs differ'
    fields = ['pages', 'text', 'pdfSha256', 'nativePreviewSha256', 'pdfiumPixelSha256']
    reviewed = json.loads((ROOT / 'tests/FullBleed.DotNet.PackageSmoke/reviewed-layout-2.5.10.json').read_text(encoding='utf-8'))
    assert reviewed['package'] == candidate['packageVersion'] and reviewed['engine'] == '2.5.10'
    assert set(reviewed['fontFixtures']) == {'invoice', 'invoice-explicit', 'bold'}
    unchanged = []
    for name, item in after.items():
        if name in reviewed['fontFixtures']:
            assert item['pages'] == before[name]['pages']
            assert [' '.join(text.split()) for text in item['text']] == [' '.join(text.split()) for text in before[name]['text']], (name, 'content changed')
            for key, value in reviewed['fontFixtures'][name].items():
                assert item[key] == value, (name, 'reviewed layout differs', key)
        else:
            for key in fields:
                assert item[key] == before[name][key], (name, key)
            unchanged.append(name)
    for key in fields:
        assert after['invoice'][key] == after['invoice-explicit'][key], ('invoice differs from explicit regular face', key)
    assert after['invoice']['text'] == before['invoice']['text']
    assert after['invoice']['pdfSha256'] != before['invoice']['pdfSha256']
    assert after['invoice']['pdfiumPixelSha256'] != before['invoice']['pdfiumPixelSha256']
    comparison = {'ok':True, 'baselinePackageVersion':VERSION, 'candidatePackageVersion':candidate['packageVersion'],
                  'unchangedFixturePdfTextAndPixels':unchanged, 'familyCases':len(families['cases']),
                  'invoice':{'beforeBytes':before['invoice']['pdfBytes'], 'afterBytes':after['invoice']['pdfBytes'],
                             'textUnchanged':True, 'matchesExplicitRegularControl':True,
                             'matchesReviewedEngine2510Layout':True, 'appearanceChangedAsIntended':True},
                  'reviewedLayoutChanges':list(reviewed['fontFixtures']),
                  'scope':'The old family-selection negative control still fails and the real invoice uses its intended regular brand face. '
                          'Fixed, reflow and Unicode fixtures remain byte-identical. Invoice, explicit-face invoice and bold text '
                          'retain normalized content and match separately reviewed 2.5.10 PDF/preview hashes. '
                          'No universal visual parity or conformance claim.'}
    (output / 'comparison.json').write_text(json.dumps(comparison, indent=2) + '\n', encoding='utf-8')
    fonts = output / 'source-fonts'
    shutil.copytree(ROOT / 'samples/FullBleed.DotNet.Showcase/Assets/fonts', fonts)
    for name in ['NotoSans-Regular.ttf', 'NotoSans-OFL.txt']:
        shutil.copyfile(ROOT / 'tests/FullBleed.DotNet.Tests/Assets' / name, fonts / name)
    print(json.dumps(comparison))


if __name__ == '__main__':
    main()
