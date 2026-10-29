# SPDX-License-Identifier: MIT
"""Reinspect public 0.2.6 endpoint failures and preserve its healthy controls."""
import argparse
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import stat
from zipfile import ZipFile

from verify_radial_gradients import verify, SOURCE

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = SOURCE.with_name('radial-gradients-public-026.json')
MANIFEST_SHA256 = '46b4174fac4a70a957615218530e510f62119492663e87663d0e4a2f9e0c9d50'


def compare(candidate, output):
    assert sha256(MANIFEST.read_bytes()).hexdigest() == MANIFEST_SHA256
    manifest = json.loads(MANIFEST.read_bytes())
    assert manifest['package'] == '0.2.6' and manifest['engine'] == '2.5.41'
    assert manifest['packageSha256'] == '007246e4aab16930417a8c097a5639f216d30de160af368b8d8d137a803bc88e'
    assert sha256(SOURCE.read_bytes()).hexdigest() == manifest['inputSha256']
    archive_path = SOURCE.with_name(manifest['archive'])
    assert sha256(archive_path.read_bytes()).hexdigest() == manifest['archiveSha256']
    baseline = output / 'baseline'
    baseline.mkdir(parents=True, exist_ok=True)
    with ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(manifest['members']) and set(archive.namelist()) == set(manifest['members'])
        for member in archive.infolist():
            relative = PurePosixPath(member.filename)
            assert not relative.is_absolute() and '..' not in relative.parts and ':' not in member.filename and '\\' not in member.filename
            assert not member.is_dir() and not stat.S_ISLNK(member.external_attr >> 16)
            data = archive.read(member)
            assert manifest['members'][member.filename] == dict(bytes=len(data), sha256=sha256(data).hexdigest())
            path = (baseline / Path(*relative.parts)).resolve()
            assert path.is_relative_to(baseline.resolve())
            if path.exists():
                assert path.read_bytes() == data
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
    previous = verify(baseline, baseline=True)
    current = json.loads((candidate / 'verification.json').read_bytes())
    assert current['ok'] and not current['baseline'] and current['packageVersion'] != '0.2.6'
    assert previous['inputSha256'] == current['inputSha256'] == manifest['inputSha256']
    assert previous['calibratedNegativeControl'] and previous['failingColorCases'] == 18 and previous['passingColorCases'] == 6
    assert sorted(row['directory'] for row in previous['cases'] if not row['outerColorCorrect']) == manifest['failedColorCases']
    before = {(row['mode'], row['name']): row for row in previous['cases']}
    after = {(row['mode'], row['name']): row for row in current['cases']}
    assert before.keys() == after.keys() and len(after) == len(current['cases']) == 24
    preserved = corrected_bounds = 0
    for key, old in before.items():
        new = after[key]
        assert new['outerColorCorrect'] and new['coreReferenceMatches'] and not new['stitchingBounds']['invalid']
        assert sha256((candidate / new['directory'] / 'document.pdf').read_bytes()).hexdigest() == new['pdfSha256']
        corrected_bounds += bool(old['stitchingBounds']['invalid'])
        if old['name'] in ('smooth-control', 'repeating-control'):
            assert old['pdfSha256'] == new['pdfSha256'] and old['nativePreviewSha256'] == new['nativePreviewSha256'], key
            assert all(old['images'][reader]['rgbSha256'] == new['images'][reader]['rgbSha256'] for reader in ('native', 'pdfium')), key
            preserved += 1
    assert preserved == 4 and corrected_bounds == 20
    record = dict(ok=True, baselinePackage='0.2.6', baselineEngine='2.5.41', candidatePackage=current['packageVersion'],
        correctedColorCases=18, correctedStitchingCases=20, unchangedPreservationControls=4,
        earlyEndingControls='Both retain correct outer colors; their previously invalid stitching bounds are corrected.',
        candidateCases=24, baselineArchiveSha256=manifest['archiveSha256'], inputSha256=manifest['inputSha256'],
        scope='Independently reinspected Windows x64/.NET 10 public-package output. Candidate platform coverage is verified separately.')
    (output / 'comparison.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    return record


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/radial-gradient-comparison')
    args = parser.parse_args()
    print(json.dumps(compare(args.candidate.resolve(), args.output.resolve())))
