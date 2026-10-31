# SPDX-License-Identifier: MIT
"""Reinspect the actual public C# font failures and preserve its healthy controls."""
import argparse
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import stat
from zipfile import ZipFile

from verify_page_margin_fonts import verify, SOURCE

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = SOURCE.with_name('page-margin-fonts-public-027.json')
MANIFEST_SHA256 = '08018be1d513ab8b19e45b09fc2b698b39f291c7fbb115f01cac5c0b50ce9357'


def compare(candidate, output):
    assert sha256(MANIFEST.read_bytes()).hexdigest() == MANIFEST_SHA256
    manifest = json.loads(MANIFEST.read_bytes())
    assert manifest['package'] == '0.2.7' and manifest['engine'] == '2.5.43'
    assert manifest['packageSha256'] == '8e530cb84dab27d77df5b86af2c27cbd51a6c2c5e415be92ed04fac66d1e20e9'
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
    assert current['ok'] and not current['baseline'] and current['packageVersion'] != '0.2.7'
    assert previous['inputSha256'] == current['inputSha256'] == manifest['inputSha256']
    assert sorted(previous['failedSelections']) == sorted(manifest['failedSelections'])
    assert len(previous['failedSelections']) == 9 and current['pairs'] == 40 and current['pages'] == 144
    after = {(row['name'], row['mode'], row['kind']): row for row in current['cases']}
    preserved = 0
    for old in previous['cases']:
        new = after[(old['name'], old['mode'], old['kind'])]
        assert new['expectedFooterFaces'] and new['expectedFooterSizes']
        assert sha256((candidate / new['directory'] / 'document.pdf').read_bytes()).hexdigest() == new['pdfSha256']
        if old['kind'] == 'control' and old['name'] != 'fallback-alias':
            for field in ['pdfSha256', 'nativePreviewSha256', 'nativePixelsSha256', 'pages']:
                assert old[field] == new[field], (old['directory'], field)
            preserved += 1
    assert preserved == 7
    record = dict(ok=True, baselinePackage='0.2.7', baselineEngine='2.5.43', candidatePackage=current['packageVersion'],
                  correctedDirectSelections=9, unchangedHealthyControls=preserved, candidatePairs=40, candidatePages=144,
                  baselineArchiveSha256=manifest['archiveSha256'], inputSha256=manifest['inputSha256'],
                  scope='The public Windows x64/.NET 8 output is reinspected on this reader host. All eight alias cases and the fallback-family control must improve; seven healthy explicit-face controls retain their PDF bytes, text and native/independent pixels. Candidate platform coverage is verified separately.')
    (output / 'comparison.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf8')
    return record


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/page-margin-font-comparison')
    args = parser.parse_args()
    print(json.dumps(compare(args.candidate.resolve(), args.output.resolve())))
