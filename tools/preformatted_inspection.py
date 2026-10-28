# SPDX-License-Identifier: MIT
"""Independent inspection of saved preformatted-block PDFs from binding consumers."""
from hashlib import sha256
from itertools import product
import json
from pathlib import Path

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium


def verify(directory, inputs_path, modes, *, baseline=False, require_previews=False, output_name='verification.json'):
    directory, inputs_path = Path(directory), Path(inputs_path)
    source = inputs_path.read_bytes()
    inputs = json.loads(source)
    fixtures = {case['name']: case for case in inputs['cases']}
    assert len(fixtures) == len(inputs['cases']) == 17, 'Incomplete or duplicate input set'
    assert modes and len(set(modes)) == len(modes), 'Explicit unique rendering modes are required'
    renders = json.loads((directory / 'renders.json').read_text(encoding='utf-8'))
    assert renders['inputSha256'] == sha256(source).hexdigest()
    assert renders['fontSha256'] == inputs['fontSha256']
    required = set(product(modes, fixtures))
    actual = [(row['mode'], row['name']) for row in renders['cases']]
    assert len(actual) == len(set(actual)) and set(actual) == required, 'Incomplete or duplicate render set'
    compact = lambda text: ''.join(text.split())
    cases, common_failures = [], []
    for row in renders['cases']:
        fixture = fixtures[row['name']]
        folder = (directory / row['directory']).resolve()
        assert folder.is_relative_to(directory.resolve()), 'Render path escapes the evidence directory'
        path = folder / 'document.pdf'
        assert sha256(path.read_bytes()).hexdigest() == row['pdfSha256'] and row['repeatPdfBytesMatch']
        reader = PdfReader(path, strict=True)
        assert len(reader.pages) == row['pages']
        native_images = []
        if require_previews:
            assert len(row['previewFiles']) == len(row['previewSha256']) == len(reader.pages)
            for filename, digest in zip(row['previewFiles'], row['previewSha256']):
                preview = (folder / filename).resolve()
                assert preview.is_relative_to(folder)
                assert sha256(preview.read_bytes()).hexdigest() == digest
                with Image.open(preview) as png:
                    assert png.size == (360, 240)
                    native_images.append(dict(width=png.width, height=png.height,
                        rgbaSha256=sha256(png.convert('RGBA').tobytes()).hexdigest()))
        text = '\n'.join(page.extract_text() for page in reader.pages)
        if compact(text) != compact(fixture['text']):
            common_failures.append({'case': row['directory'], 'reason': 'Authored text differs'})
        fonts = {}
        for page in reader.pages:
            assert abs(float(page.mediabox.width) - 180) < .001 and abs(float(page.mediabox.height) - 120) < .001
            for reference in page['/Resources']['/Font'].get_object().values():
                font = reference.get_object()
                descendant = font['/DescendantFonts'][0].get_object() if '/DescendantFonts' in font else font
                descriptor = descendant.get('/FontDescriptor')
                fonts[str(font.get('/BaseFont', ''))] = bool(descriptor and any(
                    key in descriptor.get_object() for key in ['/FontFile', '/FontFile2', '/FontFile3']))
        if not fonts or not all('NotoSans' in name and embedded for name, embedded in fonts.items()):
            common_failures.append({'case': row['directory'], 'reason': 'Unexpected or unembedded font', 'fonts': fonts})
        characters, images = [], []
        document = pdfium.PdfDocument(str(path))
        for index in range(len(document)):
            page = document[index]
            textpage = page.get_textpage()
            for offset in range(textpage.count_chars()):
                character = textpage.get_text_range(offset, 1)
                if character.strip():
                    box = list(textpage.get_charbox(offset))
                    if box[2] > box[0] and box[3] > box[1]:
                        characters.append([index, character, box])
            bitmap = page.render(scale=2)
            image = bitmap.to_pil()
            image.save(folder / f'pdfium-{index + 1}.png')
            images.append(dict(page=index + 1, width=image.width, height=image.height,
                               rgbaSha256=sha256(image.convert('RGBA').tobytes()).hexdigest()))
            image.close(); bitmap.close(); textpage.close(); page.close()
        document.close()
        assert characters
        (folder / 'characters.json').write_text(json.dumps(characters), encoding='utf-8')
        outside = [value for value in characters if value[2][0] < 11 or value[2][2] > 169
                   or value[2][1] < 11 or value[2][3] > 109]
        failures = []
        if fixture['kind'] == 'wrap':
            if len(reader.pages) <= 1:
                failures.append('Wrapping did not paginate')
            if outside:
                failures.append(f'{len(outside)} visible glyphs outside print area')
        elif fixture['kind'] == 'no-wrap':
            if len(reader.pages) != 1 or not any(value[2][2] > 180 for value in characters):
                failures.append('No-wrap behavior changed')
        else:
            assert fixture['kind'] == 'whitespace'
            first = next(value[2] for value in characters if value[1] == 'A')
            second = next(value[2] for value in characters if value[1] == 'B')
            if (first[0] > 17) != fixture['preserveIndent']:
                failures.append('Indentation policy ignored')
            if (first[1] - second[1] > 10) != fixture['preserveNewline']:
                failures.append('Newline policy ignored')
        geometry = [[p, c, [round(x, 3) for x in box]] for p, c, box in characters]
        cases.append({**row, 'kind': fixture['kind'], 'ok': not failures, 'failures': failures,
                      'outsidePrintArea': len(outside), 'fonts': fonts, 'images': images,
                      'nativeImages': native_images,
                      'textSha256': sha256(compact(text).encode()).hexdigest(),
                      'glyphGeometrySha256': sha256(json.dumps(geometry, separators=(',', ':')).encode()).hexdigest()})
    failed = {(row['mode'], row['name']) for row in cases if not row['ok']}
    expected = {(mode, fixture['name']) for mode in modes for fixture in fixtures.values()
                if fixture['kind'] == 'wrap' or (fixture['kind'] == 'whitespace' and not fixture['preserveIndent'])}
    report = dict(ok=not failed and not common_failures, baseline=baseline,
        packageVersion=renders['packageVersion'], engineVersion=renders.get('engineVersion'),
        inputSha256=sha256(source).hexdigest(), fontSha256=inputs['fontSha256'], modes=list(modes),
        cases=cases, failingCases=len(failed), passingCases=len(cases) - len(failed),
        commonFailures=common_failures, calibratedNegativeControl=baseline and failed == expected and not common_failures,
        scope='Complete explicit-font synthetic saved-PDF inputs, text, glyph bounds and whitespace policy; no broad CSS parity or conformance claim.')
    (directory / output_name).write_text(json.dumps(report, indent=2), encoding='utf-8')
    assert not common_failures, common_failures
    if baseline:
        assert failed == expected, failed ^ expected
    else:
        assert not failed, failed
    return report
