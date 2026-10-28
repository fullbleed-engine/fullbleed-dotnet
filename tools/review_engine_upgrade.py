"""Retain the reviewed 2.5.22-to-2.5.35 changes outside pagination fixtures."""
from hashlib import sha256
import json
from pathlib import Path

from pypdf import PdfReader
from pypdf.generic import ArrayObject, ByteStringObject, ContentStream, DictionaryObject, IndirectObject, StreamObject


def plain(value):
    if isinstance(value, IndirectObject):
        return {'ref': [value.idnum, value.generation]}
    if isinstance(value, StreamObject):
        return {'dictionary': {str(k): plain(v) for k, v in value.items() if k != '/Length'},
                'streamSha256': sha256(value.get_data()).hexdigest()}
    if isinstance(value, DictionaryObject):
        return {str(k): plain(v) for k, v in value.items()}
    if isinstance(value, ArrayObject):
        return [plain(v) for v in value]
    if isinstance(value, ByteStringObject):
        return value.hex()
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    return str(value)


def tagged_content(reader):
    page = reader.pages[0]
    operations = ContentStream(page.get_contents(), reader).operations
    paints = []
    text_contexts = []
    table_border_contexts = []
    stack = []
    for operands, operator in operations:
        if operator in (b'BMC', b'BDC'):
            stack.append((str(operands[0]), plain(operands[1]) if operator == b'BDC' else None))
        elif operator == b'EMC':
            assert stack, 'Unbalanced marked content'
            stack.pop()
        else:
            paints.append((plain(ArrayObject(operands)), operator.decode('ascii')))
            if operator in (b'Tj', b'TJ', b"'", b'"', b'Do'):
                text_contexts.append((list(stack), plain(ArrayObject(operands)), operator.decode('ascii')))
            if operator in (b'f', b'f*') and any(tag == '/Table' for tag, _ in stack):
                table_border_contexts.append(list(stack))
    assert not stack
    return paints, text_contexts, table_border_contexts


def review_tagged(before, after):
    previous, current = PdfReader(before), PdfReader(after)
    assert len(previous.pages) == len(current.pages) == 1
    content = previous.pages[0].raw_get('/Contents')
    assert isinstance(content, IndirectObject)
    assert plain(content) == plain(current.pages[0].raw_get('/Contents'))
    old_keys = {(g, n) for g, items in previous.xref.items() for n in items if n}
    new_keys = {(g, n) for g, items in current.xref.items() for n in items if n}
    assert old_keys == new_keys
    changed = []
    for generation, number in sorted(old_keys):
        old = previous.get_object(IndirectObject(number, generation, previous))
        new = current.get_object(IndirectObject(number, generation, current))
        if plain(old) != plain(new):
            changed.append((generation, number))
    assert changed == [(content.generation, content.idnum)], 'Unexpected PDF object change'
    # Content-derived document IDs change with the marked content stream.
    assert {k: plain(v) for k, v in previous.trailer.items() if k != '/ID'} == {
        k: plain(v) for k, v in current.trailer.items() if k != '/ID'}
    old_paints, old_text, old_borders = tagged_content(previous)
    new_paints, new_text, new_borders = tagged_content(current)
    assert old_paints == new_paints, 'Visible drawing/text operators changed'
    assert old_text == new_text, 'Text or glyph ownership changed'
    assert len(old_borders) == len(new_borders) == 8
    assert all(any(tag in ('/TH', '/TD') for tag, _ in context) and
               not any(tag == '/Artifact' for tag, _ in context) for context in old_borders)
    assert all(any(tag == '/Artifact' for tag, _ in context) and
               not any(tag in ('/TH', '/TD') for tag, _ in context) for context in new_borders)
    return dict(ok=True, changedObjects=changed, tableBorderPaints=8,
                drawingOperatorsUnchanged=True, textAndGlyphOwnershipUnchanged=True,
                structureAndOtherObjectsUnchanged=True,
                correction='Four table cells retain eight border fills, now marked as artifacts outside the cell MCIDs.')


def review(before, after, output):
    before, after = Path(before), Path(after)
    def files(directory):
        return {path.relative_to(directory).as_posix(): path for path in directory.rglob('*')
                if path.is_file() and path.suffix in ('.pdf', '.png')
                # Later fixture families have their own public-package controls.
                and path.relative_to(directory).parts[0] not in ('pagination', 'table-wrapping', 'document-regressions', 'text-decorations')
                and not any(part.startswith('pdfium-') for part in path.relative_to(directory).parts)}
    old_files, new_files = files(before), files(after)
    assert old_files.keys() == new_files.keys(), 'Legacy PDF/preview file set changed'
    differences = []
    for name in sorted(old_files):
        old_hash = sha256(old_files[name].read_bytes()).hexdigest()
        new_hash = sha256(new_files[name].read_bytes()).hexdigest()
        if old_hash != new_hash:
            differences.append(dict(path=name, previousSha256=old_hash, candidateSha256=new_hash))
    assert {row['path'] for row in differences} == {
        'tagged-structure/ua1/document.pdf', 'tagged-structure/ua2/document.pdf'}
    for row in differences:
        row['review'] = review_tagged(old_files[row['path']], new_files[row['path']])
    report = dict(ok=True, unchangedFiles=len(old_files) - len(differences), changes=differences,
                  scope='Existing PDF/native preview outputs against public 0.2.0. Only two tagged PDF streams change: table border artifact marking and content-derived IDs. Independent profile checks run separately.')
    Path(output).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report
