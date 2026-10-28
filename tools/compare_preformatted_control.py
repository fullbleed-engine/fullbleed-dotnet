"""Reinspect retained public NuGet0.2.5 PDFs and compare preservation controls."""
from hashlib import sha256
import argparse
import json
from pathlib import Path, PurePosixPath
import stat
from zipfile import ZipFile
from verify_preformatted import verify, SOURCE

ROOT=Path(__file__).resolve().parents[1]
MANIFEST=SOURCE.with_name('preformatted-public-025.json')
MANIFEST_SHA256='4491d11ba2147cf2d884383e49dad5bf8f4808cbe6339ede3f17f41fb1d36a14'

def compare(candidate, output):
    assert sha256(MANIFEST.read_bytes()).hexdigest()==MANIFEST_SHA256
    manifest=json.loads(MANIFEST.read_text(encoding='utf-8'))
    assert manifest['package']=='0.2.5' and manifest['engine']=='2.5.40'
    assert sha256(SOURCE.read_bytes()).hexdigest()==manifest['inputSha256']
    archive_path=SOURCE.with_name(manifest['archive'])
    assert sha256(archive_path.read_bytes()).hexdigest()==manifest['archiveSha256']
    baseline=output/'baseline'
    baseline.mkdir(parents=True,exist_ok=True)
    with ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist())==len(manifest['members']) and set(archive.namelist())==set(manifest['members'])
        for item in archive.infolist():
            relative=PurePosixPath(item.filename)
            assert not relative.is_absolute() and '..' not in relative.parts and ':' not in item.filename and '\\' not in item.filename
            assert item.is_dir() is False and not stat.S_ISLNK(item.external_attr>>16)
            data=archive.read(item)
            assert manifest['members'][item.filename]==dict(bytes=len(data),sha256=sha256(data).hexdigest())
            path=(baseline/Path(*relative.parts)).resolve()
            assert path.is_relative_to(baseline.resolve())
            if path.exists(): assert path.read_bytes()==data
            else:
                path.parent.mkdir(parents=True,exist_ok=True)
                path.write_bytes(data)
    previous=verify(baseline,baseline=True)
    current=json.loads((candidate/'verification.json').read_text(encoding='utf-8'))
    assert current['ok'] and not current['baseline'] and not current['commonFailures'], 'Candidate saved-PDF checks failed'
    assert previous['packageVersion']=='0.2.5' and current['packageVersion']!='0.2.5'
    assert previous['inputSha256']==current['inputSha256']==manifest['inputSha256']
    assert previous['calibratedNegativeControl'] and previous['failingCases']==30 and previous['passingCases']==21
    assert sorted(r['directory'] for r in previous['cases'] if not r['ok'])==manifest['failedCases']
    before={(r['mode'],r['name']):r for r in previous['cases']}
    after={(r['mode'],r['name']):r for r in current['cases']}
    assert before.keys()==after.keys() and len(after)==len(current['cases'])==51
    unchanged=0
    for key,old in before.items():
        new=after[key]
        assert new['ok'] and sha256((candidate/new['directory']/'document.pdf').read_bytes()).hexdigest()==new['pdfSha256']
        if old['ok']:
            assert old['pdfSha256']==new['pdfSha256'] and old['nativeImages']==new['nativeImages'],key
            unchanged+=1
    assert unchanged==21
    record=dict(ok=True,baselinePackage='0.2.5',baselineEngine='2.5.40',candidatePackage=current['packageVersion'],
        correctedCases=30,unchangedPreservationControls=21,candidateCases=51,
        baselineArchiveSha256=manifest['archiveSha256'],inputSha256=manifest['inputSha256'],
        scope='Independently reinspected retained Windows .NET10 public-package output. Candidate checks and platform coverage are verified separately.')
    (output/'comparison.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
    return record

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate',type=Path)
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/preformatted-comparison')
    args=parser.parse_args()
    print(json.dumps(compare(args.candidate.resolve(),args.output.resolve())),flush=True)
