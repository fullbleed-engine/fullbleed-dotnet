"""Generate/check the license bundle for the exact Cargo-locked bridge dependencies."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tomllib

ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / 'native/fullbleed-dotnet-native'


def collect():
    metadata = json.loads(subprocess.check_output([
        'cargo', 'metadata', '--locked', '--format-version', '1',
        '--manifest-path', str(NATIVE / 'Cargo.toml'),
    ], text=True))
    locked = tomllib.loads((NATIVE / 'Cargo.lock').read_text(encoding='utf-8'))
    checksums = {(p['name'], p['version']): p.get('checksum') for p in locked['package']}
    files = {}
    dependencies = []
    for package in sorted(metadata['packages'], key=lambda p: (p['name'], p['version'])):
        if package['source'] is None:
            continue  # The bridge itself is covered by the repository LICENSE.
        source = Path(package['manifest_path']).parent
        licenses = sorted(p for p in source.iterdir() if p.is_file() and
                          p.name.lower().startswith(('license', 'copying', 'copyright', 'notice')))
        if package['name'] == 'fullbleed':
            # The core's compiled preview outlines include OFL font data. Keep
            # their notices and modification/provenance record in the NuGet too.
            notices = [source / 'THIRD_PARTY_LICENSES.md', source / 'src/preview_fonts/README.md',
                       source / 'src/preview_fonts/sources.json']
            notices.extend(sorted((source / 'src/preview_fonts').glob('LICENSE-*.txt')))
            assert len(notices) == 8 and all(path.is_file() for path in notices), 'Missing bundled preview-font notices'
            licenses.extend(notices)
        if not licenses:
            raise RuntimeError(f"No upstream license text found for {package['name']}")
        record = {'name': package['name'], 'version': package['version'],
                  'license': package['license'], 'repository': package['repository'],
                  'crate_sha256': checksums[(package['name'], package['version'])],
                  'license_files': []}
        for license_file in licenses:
            relative = Path('licenses/native') / f"{package['name']}-{package['version']}" / license_file.relative_to(source)
            content = license_file.read_bytes()
            files[relative] = content
            record['license_files'].append({'path': relative.as_posix(),
                                           'sha256': hashlib.sha256(content).hexdigest()})
        dependencies.append(record)
    files[Path('native-provenance.json')] = (json.dumps({
        'schema': 'fullbleed.dotnet.native_provenance.v1',
        'cargo_lock_sha256': hashlib.sha256((NATIVE / 'Cargo.lock').read_bytes()).hexdigest(),
        'scope': 'Cargo-locked native bridge graph, including build-time proc macros',
        'dependencies': dependencies,
    }, indent=2, ensure_ascii=False) + '\n').encode('utf-8')
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    files = collect()
    for relative, content in files.items():
        path = ROOT / relative
        if args.check:
            if not path.is_file() or path.read_bytes() != content:
                raise SystemExit(f'Stale native provenance: {relative.as_posix()}')
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
    actual = {p.relative_to(ROOT) for p in (ROOT / 'licenses/native').rglob('*') if p.is_file()}
    expected = {p for p in files if p.parts[0] == 'licenses'}
    if actual != expected:
        raise SystemExit('Unexpected files in licenses/native; inspect the stale license bundle.')
    print(f"Native provenance {'verified' if args.check else 'generated'}: {len(files)} files")


if __name__ == '__main__':
    main()
