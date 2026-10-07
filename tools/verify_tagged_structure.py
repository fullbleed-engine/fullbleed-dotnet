"""Independently check the retained tagged specimen; no application dependency."""
import argparse
from hashlib import sha256
import io
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from pypdf import PdfReader

ROOT = Path(__file__).resolve().parents[1]
URL = 'https://software.verapdf.org/rel/1.30/verapdf-greenfield-1.30.2-installer.zip'
INSTALLER_SHA256 = '6cc6341cb1af644044054b81f00a6590a7918abb18f762243de115258bcad838'


def validator(cache):
    cache.mkdir(parents=True, exist_ok=True)
    installer = cache / 'verapdf-1.30.2.zip'
    if not installer.exists():
        with urlopen(URL, timeout=120) as response:
            data = response.read()
        assert sha256(data).hexdigest() == INSTALLER_SHA256, 'veraPDF download checksum differs'
        installer.write_bytes(data)
    assert sha256(installer.read_bytes()).hexdigest() == INSTALLER_SHA256
    classes = cache / 'classes'
    if not (classes / 'org/verapdf/apps/GreenfieldCliWrapper.class').is_file():
        with zipfile.ZipFile(installer) as archive:
            jars = [name for name in archive.namelist() if name.endswith('.jar')]
            assert len(jars) == 1
            jar_bytes = archive.read(jars[0])
        with zipfile.ZipFile(io.BytesIO(jar_bytes)) as jar:
            packs = [name for name in jar.namelist() if name.endswith('pack-veraPDF CLI')]
            assert len(packs) == 1
            pack_bytes = jar.read(packs[0])
        with zipfile.ZipFile(io.BytesIO(pack_bytes)) as pack:
            for info in pack.infolist():
                destination = (classes / info.filename).resolve()
                assert destination.is_relative_to(classes.resolve()), 'Unsafe validator archive path'
            pack.extractall(classes)
    return classes


def verify(directory, classpath, *, require_compliant=True):
    data = json.loads((directory / 'renders.json').read_text(encoding='utf-8-sig'))
    assert [case['name'] for case in data['cases']] == ['none', 'ua1', 'ua2']
    for file, key in [('input.html', 'htmlSha256'), ('style.css', 'cssSha256')]:
        assert sha256((directory / file).read_bytes()).hexdigest() == data[key]
    version = subprocess.check_output(['java', '-cp', str(classpath), 'org.verapdf.apps.GreenfieldCliWrapper', '--version'], text=True)
    assert version.startswith('veraPDF 1.30.2\n'), version
    records = []
    for case in data['cases']:
        folder = directory / case['name']
        pdf = folder / 'document.pdf'
        assert sha256(pdf.read_bytes()).hexdigest() == case['pdfSha256']
        assert sha256((folder / case['previewFile']).read_bytes()).hexdigest() == case['previewSha256']
        assert case['repeatPdfBytesIdentical'] and not case['diagnostics']['MissingGlyphs']
        reader = PdfReader(pdf, strict=True)
        assert len(reader.pages) == 1
        text = ' '.join(reader.pages[0].extract_text().split())
        for phrase in ['A place to begin', 'Your visit', 'What to bring', 'How to join',
                       'Figure 1. The sample rectangle.', 'Figure 2. A second rectangle, with its caption first.']:
            assert text.count(phrase) == 1, (case['name'], phrase)
        record = {key: case[key] for key in ['name', 'pdfSha256', 'previewSha256']}
        record['text'] = text
        if case['name'] != 'none':
            root = reader.trailer['/Root']
            assert root['/Lang'] == 'en-US' and root['/MarkInfo']['/Marked']
            assert root['/StructTreeRoot'].get('/K') is not None
            with (folder / 'verapdf.json').open('w', encoding='utf-8') as out, (folder / 'verapdf.stderr.txt').open('w', encoding='utf-8') as err:
                result = subprocess.run(['java', '-cp', str(classpath), 'org.verapdf.apps.GreenfieldCliWrapper',
                    '--format', 'json', '--maxfailuresdisplayed', '-1', '-f', case['name'], str(pdf)],
                    stdout=out, stderr=err, timeout=120)
            parsed = json.loads((folder / 'verapdf.json').read_text(encoding='utf-8'))
            report = parsed['report']['jobs'][0]['validationResult'][0]
            assert result.returncode in (0, 1) and report['jobEndStatus'] == 'normal', 'Validator failed to execute'
            if report['compliant']:
                assert result.returncode == 0
            record['veraPDF'] = {'version': '1.30.2', 'compliant': report['compliant'],
                'exitCode': result.returncode, 'failedRules': report['details']['failedRules'], 'failedChecks': report['details']['failedChecks']}
        records.append(record)
    report = {'ok': all(c.get('veraPDF', {}).get('compliant', True) for c in records),
        'packageVersion': data['packageVersion'],
        'inputs': {key: data[key] for key in ['htmlSha256', 'cssSha256', 'embeddedFontSha256']},
        'validator': {'url': URL, 'installerSha256': INSTALLER_SHA256, 'version': '1.30.2'},
        'cases': records,
        'scope': 'Retained specimen and machine-verifiable PDF/UA checks. Content and reading-order review remain necessary.'}
    (directory / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if require_compliant:
        assert report['ok'], 'Tagged specimen failed veraPDF; retained report has the failed rules'
        assert all(c.get('veraPDF', {}).get('failedChecks', 0) == 0 for c in records)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--verapdf-cp', type=Path)
    parser.add_argument('--validator-cache', type=Path, default=ROOT / 'artifacts/verapdf')
    args = parser.parse_args()
    report = verify(args.directory.resolve(), args.verapdf_cp or validator(args.validator_cache.resolve()))
    print(json.dumps({'ok': report['ok'], 'cases': len(report['cases'])}))


if __name__ == '__main__':
    main()
