"""Independently read the invoice and compiled records produced by a package consumer."""
import argparse
import json
from pathlib import Path

import pypdf


def verify(directory: Path):
    invoice = pypdf.PdfReader(directory / 'invoice.pdf', strict=True)
    assert len(invoice.pages) == 1, 'Invoice must contain exactly one page'
    text = ' '.join(invoice.pages[0].extract_text().split())
    # Preserve the content of the designed invoice as well as its page count.
    expected = [
        'Maple & Finch',
        'Discovery & brand workshop',
        'Digital design & implementation',
        'Review & studio handoff',
    ]
    for phrase in expected:
        assert text.count(phrase) == 1, f'Expected one searchable copy of {phrase!r}'
    assert text.count('$1,870.00') == 2, 'Subtotal and total must remain searchable'
    # The invoice uses weight 600. This separate weight-700 fixture exercises
    # simulated bold, which duplicated searchable text before engine 2.5.6.
    bold = pypdf.PdfReader(directory / 'bold.pdf', strict=True)
    assert len(bold.pages) == 1, 'Bold fixture must contain exactly one page'
    assert ' '.join(bold.pages[0].extract_text().split()) == (
        'Invoice BOLD-1042 Customer Ada Total USD 250.00'
    ), 'Bold text must extract once, with its original spacing'
    records = pypdf.PdfReader(directory / 'records.pdf', strict=True)
    assert len(records.pages) == 2, 'Compiled output must contain two records'
    for page, identifier in zip(records.pages, ['FIRST-001', 'SECOND-002']):
        assert ' '.join(page.extract_text().split()) == f'Invoice {identifier}'
    return {
        'reader': f'pypdf {pypdf.__version__}',
        'invoice_pages': len(invoice.pages),
        'unique_styled_runs': len(expected),
        'total_amount_occurrences': 2,
        'single_copy_bold_text': True,
        'compiled_records': len(records.pages),
        'status': 'passed',
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.directory), indent=2))
