"""Require the candidate to correct the exact retained public NuGet font failures."""
import argparse
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import zipfile
from verify_font_cascade import verify, SOURCE

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets'

def compare(candidate, output=None):
    output = Path(output or ROOT / 'artifacts/font-cascade-comparison')
    manifest = json.loads((ASSETS / 'font-cascade-public-0210.json').read_bytes())
    archive_path = ASSETS / 'font-cascade-public-0210.zip'
    assert sha256(archive_path.read_bytes()).hexdigest() == manifest['archiveSha256']
    assert manifest['packageVersion'] == '0.2.10' and manifest['engineVersion'] == '2.5.50'
    assert manifest['actualLoadedNativeMatchesPackage'] is True
    assert manifest['fixtureSha256'] == sha256(SOURCE.read_bytes()).hexdigest()
    baseline = output / 'baseline'
    baseline.mkdir(parents=True, exist_ok=False)
    with zipfile.ZipFile(archive_path) as archive:
        assert len(archive.namelist()) == len(set(archive.namelist())) == len(manifest['files'])
        assert set(archive.namelist()) == set(manifest['files'])
        for name in archive.namelist():
            path = PurePosixPath(name)
            assert not path.is_absolute() and len(path.parts) == 1 and '\\' not in name and '..' not in path.parts
            data = archive.read(name)
            assert sha256(data).hexdigest() == manifest['files'][name]
            (baseline / name).write_bytes(data)
    before = verify(baseline, negative_control=True)
    after = verify(candidate)
    assert {case['name']: case['actualBaseFonts'] for case in before['cases']} == manifest['actualBaseFonts']
    assert before['failures'] == manifest['failingCases'] and len(before['failures']) == 11
    corrected = {case['name']: case for case in after['cases']}
    healthy = [case for case in before['cases'] if case['matchesExpected']]
    assert len(healthy) == 2 and all(case['matchesExpected'] for case in corrected.values())
    assert all(corrected[case['name']]['actualBaseFonts'] == case['actualBaseFonts'] for case in healthy)
    result = {'status': 'passed', 'candidatePackage': after['packageVersion'], 'baselinePackage': '0.2.10',
        'baselinePackageSha256': manifest['packageSha256'], 'actualPublicNativeSha256': manifest['actualLoadedNativeSha256'],
        'correctedCases': 11, 'healthyControls': 2, 'fixtureSha256': manifest['fixtureSha256'],
        'publicFailures': before['failures'], 'allCandidateFontSelectionsCorrect': True}
    (output / 'verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    print(json.dumps(compare(args.candidate)))
