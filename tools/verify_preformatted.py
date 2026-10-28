# SPDX-License-Identifier: MIT
"""Inspect saved preformatted-block output from C# package consumers."""
import argparse
import json
from pathlib import Path
from preformatted_inspection import verify as inspect

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'tests/FullBleed.DotNet.PackageSmoke/Assets/preformatted-whitespace-2.5.41.json'

def verify(directory, *, baseline=False):
    return inspect(directory,SOURCE,['direct','compiled','reflow'],baseline=baseline,require_previews=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path)
    parser.add_argument('--negative-control',action='store_true')
    args=parser.parse_args()
    report=verify(args.directory,baseline=args.negative_control)
    print(json.dumps({k:report[k] for k in ['ok','baseline','packageVersion','passingCases','failingCases','calibratedNegativeControl']}))
