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
MANIFEST = SOURCE.with_name('generated-counters-public-028.json')
MANIFEST_SHA256 = 'f42e525b9a85e7f83de2d7c1304922942a0e978caef00ac382dfb7dc6039a033'


def compare(candidate, output):
    assert sha256(MANIFEST.read_bytes()).hexdigest() == MANIFEST_SHA256
    manifest = json.loads(MANIFEST.read_bytes())
    assert manifest['package'] == '0.2.8' and manifest['engine'] == '2.5.48'
    assert manifest['packageSha256'] == '3374481dcae686c894bd059cea972660f5365e862b079f1078e173210a524b0b'
    assert manifest['nativeSha256'] == '4456f2e15b6ecf6ba3a5ef374f7f465bfa622e8fc3544c76807dd212e46f8176'
    assert manifest['nativeModuleVerifiedAgainstPackage']
    assert sha256(SOURCE.read_bytes()).hexdigest() == manifest['inputSha256']
    archive_path = SOURCE.with_name(manifest['archive'])
    assert sha256(archive_path.read_bytes()).hexdigest() == manifest['archiveSha256']
    baseline = output / 'baseline'
    baseline.mkdir(parents=True, exist_ok=True)
    with ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(manifest['members']) == 62
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
    assert tuple(map(int, current['packageVersion'].split('.'))) >= (0, 2, 9)
    assert previous['fixtureSha256'] == current['fixtureSha256'] == manifest['inputSha256']
    assert sorted(previous['failures']) == sorted(manifest['failedCases'])
    assert len(previous['failures']) == 47 and current['documents'] == 295 and current['pages'] == 480
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
    assert corrected == 47 and preserved == 12
    record = dict(ok=True, baselinePackage='0.2.8', baselineEngine='2.5.48', candidatePackage=current['packageVersion'],
        correctedDirectCases=corrected, unchangedHealthyCounterSequences=preserved,
        byteIdenticalHealthyControls=byte_identical_healthy, candidateDocuments=295, candidatePages=480,
        baselineArchiveSha256=manifest['archiveSha256'], inputSha256=manifest['inputSha256'],
        scope='The retained public Windows x64/.NET 8 output is independently reinspected. All 47 wrong direct cases must improve, while 12 healthy counter sequences and page counts remain unchanged. Candidate platform coverage is verified separately; raw PDF text-stream order is not a logical reading-order claim.')
    (output / 'comparison.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf8')
    return record


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/generated-counter-comparison')
    args = parser.parse_args()
    print(json.dumps(compare(args.candidate.resolve(), args.output.resolve())))
