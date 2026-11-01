# SPDX-License-Identifier: MIT
"""Require correction of retained public C# counter failures and healthy controls."""
import argparse
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import stat
from zipfile import ZipFile

from verify_generated_counters import verify, SOURCE

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = SOURCE.with_name('generated-counters-public-029.json')
MANIFEST_SHA256 = 'd0fcad561975092f4f5316932f6608ca2c83d1d7686bea16a69746a2e058e895'


def compare(candidate, output):
    assert sha256(MANIFEST.read_bytes()).hexdigest() == MANIFEST_SHA256
    manifest = json.loads(MANIFEST.read_bytes())
    assert manifest['package'] == '0.2.9' and manifest['engine'] == '2.5.49'
    assert manifest['packageSha256'] == '6c2fc070907654594e55ee1407f7ef44624892242d4e84f148251eb951d4fee3'
    assert manifest['nativeSha256'] == 'dc0f9bddf254c0ec64aff19abcab6f553083e6159c9afdfbfbd33d33a1b71664'
    assert manifest['nativeModuleVerifiedAgainstPackage']
    assert sha256(SOURCE.read_bytes()).hexdigest() == manifest['inputSha256']
    archive_path = SOURCE.with_name(manifest['archive'])
    assert sha256(archive_path.read_bytes()).hexdigest() == manifest['archiveSha256']
    baseline = output / 'baseline'
    baseline.mkdir(parents=True, exist_ok=True)
    with ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(manifest['members']) == 14
        assert set(archive.namelist()) == set(manifest['members'])
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
    previous = verify(baseline, negative_control=True)
    current = json.loads((candidate / 'verification.json').read_bytes())
    assert current['ok'] and not current['negativeControl']
    assert tuple(map(int, current['packageVersion'].split('.'))) >= (0, 2, 10)
    assert previous['fixtureSha256'] == current['fixtureSha256'] == manifest['inputSha256']
    assert sorted(previous['failures']) == sorted(manifest['failedCases'])
    assert len(previous['failures']) == 7 and current['documents'] == 350 and current['pages'] == 576
    after = {(row['name'], row['mode']): row for row in current['cases']}
    corrected = preserved = byte_identical_healthy = 0
    for old in previous['cases']:
        new = after[(old['name'], 'direct')]
        assert new['matchesExpected'] and new['independentReadersAgree'] and new['recordRangesVerified']
        assert sha256((candidate / (new['name'] + '-direct.pdf')).read_bytes()).hexdigest() == new['pdfSha256']
        if old['matchesExpected']:
            assert (old['actual'], old['pages']) == (new['actual'], new['pages'])
            preserved += 1
            byte_identical_healthy += old['pdfSha256'] == new['pdfSha256']
        else:
            corrected += 1
    assert corrected == 7 and preserved == 4
    record = dict(ok=True, baselinePackage='0.2.9', baselineEngine='2.5.49', candidatePackage=current['packageVersion'],
        correctedDirectCases=corrected, unchangedHealthyCounterSequences=preserved,
        byteIdenticalHealthyControls=byte_identical_healthy, candidateDocuments=350, candidatePages=576,
        baselineArchiveSha256=manifest['archiveSha256'], inputSha256=manifest['inputSha256'],
        scope='The retained public Windows x64/.NET 10 output is independently reinspected. All seven wrong direct cases must improve, while four healthy counter sequences and page counts remain unchanged. Candidate platform coverage is verified separately; raw PDF text-stream order is not a logical reading-order claim.')
    (output / 'comparison.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf8')
    return record


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/generated-counter-comparison')
    args = parser.parse_args()
    print(json.dumps(compare(args.candidate.resolve(), args.output.resolve())))
