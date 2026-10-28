"""Calibrate the candidate with retained Windows output from public NuGet 0.2.3."""
import argparse
from hashlib import sha256
from io import BytesIO
import json
from pathlib import Path, PurePosixPath
import stat
import zipfile

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium

from decoration_inspection import check_image, glyph_geometry
from verify_text_decorations import SOURCE, SOURCE_SHA256, geometry_digest

ROOT = Path(__file__).resolve().parents[1]
ASSETS = SOURCE.parent
MANIFEST = ASSETS / 'text-decorations-public-023.json'
MANIFEST_SHA256 = '0fd2bcb8cba8488792a762d1316140417e172f694b66fa341ae63d77bc26f7ee'


def compare(candidate):
    assert sha256(MANIFEST.read_bytes()).hexdigest() == MANIFEST_SHA256
    assert sha256(SOURCE.read_bytes()).hexdigest() == SOURCE_SHA256
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    inputs = json.loads(SOURCE.read_text(encoding='utf-8'))
    current = json.loads((candidate / 'verification.json').read_text(encoding='utf-8'))
    assert current['ok'] and current['sourceSha256'] == SOURCE_SHA256 and len(current['cases']) == 80
    direct = {row['name']: row for row in current['cases'] if row['mode'] == 'direct'}
    archive_path = ASSETS / manifest['archive']
    assert sha256(archive_path.read_bytes()).hexdigest() == manifest['archiveSha256']
    rows = []
    with zipfile.ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == set(manifest['members']) and len(archive.namelist()) == len(manifest['members'])
        for item in archive.infolist():
            path = PurePosixPath(item.filename)
            assert not path.is_absolute() and '..' not in path.parts and ':' not in item.filename and '\\' not in item.filename
            assert not stat.S_ISLNK(item.external_attr >> 16)
            data = archive.read(item)
            assert len(data) == manifest['members'][item.filename]['bytes']
            assert sha256(data).hexdigest() == manifest['members'][item.filename]['sha256']
        renders = json.loads(archive.read('renders.json'))
        assert renders['packageVersion'] == manifest['package'] == '0.2.3'
        assert renders['nativeSha256'] == manifest['nativeSha256']
        assert renders['fontSha256'] == inputs['fontSha256']
        assert renders['originalReportSha256'] == manifest['originalRenderReportSha256']
        for fixture, rendered in zip(inputs['cases'], renders['cases'], strict=True):
            name = fixture['name']
            assert rendered['name'] == name and rendered['deterministic'] and rendered['missingGlyphs'] == 0
            expected = fixture['expected']['direct']
            pdf = archive.read(name + '/document.pdf')
            assert sha256(pdf).hexdigest() == rendered['pdfSha256'] == fixture['public023DirectPdfSha256']
            assert archive.read(name + '/input.html').decode('utf-8') == fixture['html'].replace('{{record}}', 'AAA')
            assert archive.read(name + '/style.css').decode('utf-8') == fixture['css']
            text = [''.join(page.extract_text().split()) for page in PdfReader(BytesIO(pdf), strict=True).pages]
            assert text == expected['text']
            geometry = glyph_geometry(pdf)
            assert geometry == glyph_geometry(archive.read(name + '/undecorated.pdf'))
            assert geometry_digest(geometry) == expected['glyphGeometrySha256']
            checks = {}

            def check(image, key):
                try:
                    checks[key] = check_image(image, fixture) == expected['images'][key]
                except AssertionError:
                    checks[key] = False

            with pdfium.PdfDocument(pdf) as document:
                assert len(document) == 1
                page = document[0]
                bitmap = page.render(scale=2)
                image = bitmap.to_pil()
                check(image, 'pdfium-1')
                image.close()
                bitmap.close()
                page.close()
            for file, key, sha_key in [('native.png', 'native', 'nativeSha256'), ('finalized.png', 'finalized-1', 'finalizedSha256')]:
                data = archive.read(name + '/' + file)
                assert sha256(data).hexdigest() == rendered[sha_key]
                with Image.open(BytesIO(data)) as image:
                    check(image, key)
            healthy = all(checks.values())
            assert healthy == fixture['public023DirectPasses']
            if healthy:
                assert rendered['pdfSha256'] == direct[name]['pdfSha256']
                assert rendered['nativeSha256'] == direct[name]['nativeSha256']
                assert rendered['finalizedSha256'] == direct[name]['previewsSha256'][0]
            rows.append(dict(name=name, publicControlPasses=healthy, independentChecks=checks,
                publicPdfSha256=rendered['pdfSha256'], candidatePdfSha256=direct[name]['pdfSha256'],
                healthyOutputUnchanged=healthy))
    failures = [row['name'] for row in rows if not row['publicControlPasses']]
    assert failures == inputs['publicBaseline']['failedDirectCases'] == manifest['failedCases'] and len(failures) == 13
    assert sum(row['healthyOutputUnchanged'] for row in rows) == 3
    return dict(ok=True, baselinePackage='0.2.3', baselineEngine='2.5.39', candidatePackage=current['packageVersion'],
        baselineArchiveSha256=manifest['archiveSha256'], cases=rows, correctedDirectCases=13, unchangedHealthyDirectCases=3,
        candidateCases=80, candidatePages=128,
        scope='Independent inspection of the retained Windows .NET 8 public-package controls; no old engine rerender or new registry download. Thirteen known direct failures are rejected and three healthy PDF/native-preview controls remain byte-identical. All eighty candidate modes are verified separately.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/text-decoration-comparison.json')
    args = parser.parse_args()
    report = compare(args.candidate.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(ok=True, correctedDirectCases=13, unchangedHealthyDirectCases=3, candidateCases=80)))
