"""Calibrate the new document checks against public NuGet 0.2.2."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from verify_document_regressions import PREVIEW_SOURCE, verify

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.2.2'
PACKAGE_SHA256 = '6926462d1578e03e6de61015e2e3877c83079b83fc1b35d7859d763a3e4e2429'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'
FAILED_QUOTATIONS = frozenset("""
inline-close-after-zero-probe
inline-visible-close-zero-probe
inline-balanced-after
contents-nested
contents-silent-before
contents-silent-after
contents-close-after-zero-probe
contents-visible-close-zero-probe
contents-balanced-after
block-nested
block-silent-before
block-silent-after
block-close-after-zero-probe
block-visible-close-zero-probe
block-balanced-after
inline-block-nested
inline-block-silent-before
inline-block-silent-after
inline-block-close-after-zero-probe
inline-block-visible-close-zero-probe
inline-block-balanced-after
rule-before
input-before
textarea-before
select-before
""".split())

def compare(previous, current):
    assert previous['packageVersion'] == VERSION and current['packageVersion'] != VERSION
    assert previous['previewSourceSha256'] == current['previewSourceSha256']
    assert previous['quoteSourceSha256'] == current['quoteSourceSha256']
    assert current['ok'] and not previous['ok']
    source = json.loads(PREVIEW_SOURCE.read_text(encoding='utf-8'))
    assert previous['failedPreviewPages'] == source['publicBaseline']['failedPages']
    assert len(previous['failedPreviewPages']) == 9
    assert len(FAILED_QUOTATIONS) == 25
    expected_quotes = {mode + '/' + name for mode in ['direct', 'compiled', 'reflow'] for name in FAILED_QUOTATIONS}
    assert set(previous['failedQuotationCases']) == expected_quotes and len(previous['failedQuotationCases']) == 75
    healthy_previews, corrected_previews, healthy_quotes = [], [], []
    for old, new, fixture in zip(previous['previewCases'], current['previewCases'], source['cases'], strict=True):
        for key in ['name', 'pages', 'pdfSha256', 'pdfiumPixelsSha256', 'text']:
            assert old[key] == new[key], (old['name'], key, 'Imported source content changed')
        assert old['previewMatches'] == fixture['public022PreviewMatches']
        assert all(new['previewMatches'])
        for index, healthy in enumerate(old['previewMatches']):
            label = old['name'] + '-page-' + str(index + 1) + '.png'
            if healthy:
                assert old['previewsSha256'][index] == new['previewsSha256'][index], label
                healthy_previews.append(label)
            else:
                assert old['previewsSha256'][index] != new['previewsSha256'][index], label
                corrected_previews.append(label)
    for old, new in zip(previous['quotationCases'], current['quotationCases'], strict=True):
        for key in ['name', 'mode', 'pages', 'htmlSha256', 'cssSha256', 'expectedText']:
            assert old[key] == new[key], (old['name'], old['mode'], key)
        assert new['ok']
        label = old['mode'] + '/' + old['name']
        if old['name'] in FAILED_QUOTATIONS:
            assert not old['ok'] and old['text'] != new['text'], label
        else:
            assert old['ok'], label
            for key in ['pdfSha256', 'previewsSha256', 'pdfiumPixelsSha256', 'text']:
                assert old[key] == new[key], (label, key, 'Healthy quotation control changed')
            healthy_quotes.append(label)
    direct = [case for case in current['quotationCases'] if case['mode'] == 'direct']
    assert len(direct) == 41 and all(case['directPdfMatchesQualifiedCore'] for case in direct)
    assert len(healthy_previews) == 34 and len(corrected_previews) == 9 and len(healthy_quotes) == 48
    return dict(ok=True, baselinePackage=VERSION, baselineEngine='2.5.37', candidatePackage=current['packageVersion'],
        sourcePdfCount=39, sourcePdfBytesUnchanged=True, correctedPreviewPages=corrected_previews,
        unchangedHealthyPreviewPages=healthy_previews, correctedQuotationCases=previous['failedQuotationCases'],
        unchangedHealthyQuotationCases=healthy_quotes, candidateQuotationDocuments=123, candidateQuotationPages=164,
        directPdfsMatchQualifiedCore=41, scope=current['scope'])

def compare_legacy(before, after):
    def inventory(directory):
        return {path.relative_to(directory).as_posix(): sha256(path.read_bytes()).hexdigest()
                for path in directory.rglob('*') if path.is_file() and path.suffix in ['.pdf', '.png']
                and path.relative_to(directory).parts[0] not in ['document-regressions', 'text-decorations']
                and not any(part.startswith('pdfium') for part in path.relative_to(directory).parts)}
    previous, current = inventory(before), inventory(after)
    assert len(previous) > 100, 'Missing earlier fixture output'
    assert previous.keys() == current.keys(), 'Earlier fixture file set changed'
    assert previous == current, [name for name in previous if previous[name] != current[name]]
    return dict(ok=True, unchangedFiles=len(previous),
        scope='All earlier fixture PDFs and native PNGs remain byte-identical to public 0.2.2; the new document-regressions family is checked separately.')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/document-regression-comparison')
    parser.add_argument('--baseline-package', type=Path)
    parser.add_argument('--baseline-consumer', type=Path)
    parser.add_argument('--shell', default='pwsh')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    packages = output / 'baseline-packages'
    packages.mkdir()
    if args.baseline_package:
        data = args.baseline_package.read_bytes()
    else:
        with urlopen(URL, timeout=60) as response:
            data = response.read()
    assert sha256(data).hexdigest() == PACKAGE_SHA256, 'The previous public package changed'
    package = packages / f'FullBleed.DotNet.{VERSION}.nupkg'
    package.write_bytes(data)
    with zipfile.ZipFile(package) as archive:
        provenance = json.loads(archive.read('native-provenance.json'))
        core, = [row for row in provenance['dependencies'] if row['name'] == 'fullbleed']
        assert core['version'] == '2.5.37' and core['crate_sha256'] == '27dddfb2458a2bc5eb4e3535ce0104535fb6a5e55b146a494883fd52e53d628e'
    (output / 'baseline-source.json').write_text(json.dumps(dict(url=URL, sha256=PACKAGE_SHA256,
        bytes=len(data), engine='2.5.37'), indent=2) + '\n', encoding='utf-8')
    baseline = args.baseline_consumer.resolve() if args.baseline_consumer else output / 'baseline'
    if not args.baseline_consumer:
        command = [args.shell, '-NoProfile', '-File', str(ROOT / 'scripts/package-smoke.ps1'), '-SkipPack',
            '-PackageVersion', VERSION, '-PackageDirectory', str(packages), '-OutputDirectory', str(baseline)]
        with (output / 'baseline-consumer.log').open('x', encoding='utf-8') as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    identity = json.loads((baseline / 'package.json').read_text(encoding='utf-8-sig'))
    assert identity['sha256'] == PACKAGE_SHA256
    previous = verify(baseline / 'document-regressions', require_correct=False)
    current = verify(args.candidate.resolve() / 'document-regressions')
    report = compare(previous, current)
    report['legacyOutputComparison'] = compare_legacy(baseline, args.candidate.resolve())
    (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(ok=True, correctedPreviewPages=9, correctedQuotationCases=75,
        unchangedHealthyPreviewPages=34, unchangedHealthyQuotationCases=48,
        unchangedEarlierFiles=report['legacyOutputComparison']['unchangedFiles'])))

if __name__ == '__main__':
    main()
