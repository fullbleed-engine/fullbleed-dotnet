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

VERSION = '0.1.2'
PACKAGE_SHA256 = '03b9e328166533145d58ececd6b69ea838973e76a71a9ef15b85e0d3b67585f1'
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
        assert next(item for item in provenance['dependencies'] if item['name'] == 'fullbleed')['version'] == '2.5.6'
    (output / 'baseline-source.json').write_text(json.dumps(
        {'url': URL, 'sha256': PACKAGE_SHA256, 'bytes': len(data), 'engine': '2.5.6'}, indent=2) + '\n', encoding='utf-8')
    baseline = output / 'baseline'
    command = [args.shell, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'scripts/package-smoke.ps1'),
               '-SkipPack', '-PackageVersion', VERSION, '-PackageDirectory', str(packages),
               '-OutputDirectory', str(baseline)]
    with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
        subprocess.run(command, cwd=ROOT, check=True, stdout=log, stderr=subprocess.STDOUT)
    previous = verify(baseline, legacy=True)
    assert previous['packageVersion'] == VERSION
    try:
        verify(baseline, report_path=output / 'negative-control.json')
    except AssertionError as error:
        assert str(error) == 'Embedded post metadata was not compacted', str(error)
        (output / 'negative-control.txt').write_text(
            'Expected rejection of the old font metadata: ' + str(error) + '\n', encoding='utf-8')
    else:
        raise AssertionError('The previous package unexpectedly passed the new compact-metadata gate')
    candidate = verify(args.candidate, baseline=baseline, report_path=output / 'comparison.json')
    assert candidate['packageVersion'] != VERSION
    fonts = output / 'source-fonts'
    shutil.copytree(ROOT / 'samples/FullBleed.DotNet.Showcase/Assets/fonts', fonts)
    for name in ['NotoSans-Regular.ttf', 'NotoSans-OFL.txt']:
        shutil.copyfile(ROOT / 'tests/FullBleed.DotNet.Tests/Assets' / name, fonts / name)
    print(json.dumps({'ok': True, 'fixtures': [
        {key: fixture[key] for key in ['name', 'pages', 'beforePdfBytes', 'pdfBytes', 'reductionPercent']}
        for fixture in candidate['fixtures']]}))


if __name__ == '__main__':
    main()
