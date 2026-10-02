"""Inspect the distributable NuGet archive, including native architectures and notices."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RIDS = {
    'win-x64': 'fullbleed_dotnet_native.dll',
    'linux-x64': 'libfullbleed_dotnet_native.so',
    'osx-x64': 'libfullbleed_dotnet_native.dylib',
    'osx-arm64': 'libfullbleed_dotnet_native.dylib',
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def architecture(rid, data):
    if rid == 'win-x64':
        assert data[:2] == b'MZ', 'Expected a PE library'
        offset = struct.unpack_from('<I', data, 0x3C)[0]
        assert data[offset:offset + 4] == b'PE\0\0'
        assert struct.unpack_from('<H', data, offset + 4)[0] == 0x8664, 'Expected x64 PE'
    elif rid == 'linux-x64':
        assert data[:6] == b'\x7fELF\x02\x01', 'Expected a 64-bit little-endian ELF library'
        assert struct.unpack_from('<H', data, 18)[0] == 62, 'Expected x64 ELF'
    else:
        assert data[:4] == b'\xcf\xfa\xed\xfe', 'Expected a 64-bit little-endian Mach-O library'
        expected = 0x1000007 if rid == 'osx-x64' else 0x100000C
        assert struct.unpack_from('<I', data, 4)[0] == expected, f'Wrong Mach-O CPU for {rid}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--all-rids', action='store_true')
    args = parser.parse_args()
    version = ET.parse(ROOT / 'src/FullBleed.DotNet/FullBleed.DotNet.csproj').findtext('.//Version')
    central = ET.parse(ROOT / 'Directory.Packages.props').find('.//PackageVersion[@Include="FullBleed.DotNet"]')
    assert central.get('Version') == version
    packages = ROOT / 'artifacts/packages'
    package = packages / f'FullBleed.DotNet.{version}.nupkg'
    symbols = packages / f'FullBleed.DotNet.{version}.snupkg'
    assert symbols.is_file(), 'Missing symbol package'
    provenance = json.loads((ROOT / 'native-provenance.json').read_text())
    engine = next(p for p in provenance['dependencies'] if p['name'] == 'fullbleed')
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    assets = []
    with zipfile.ZipFile(package) as z:
        assert z.testzip() is None
        assert len(z.namelist()) == len(set(z.namelist())), 'Duplicate package paths'
        metadata = ET.fromstring(z.read('FullBleed.DotNet.nuspec'))
        ns = {'n': metadata.tag.split('}')[0].strip('{')}
        assert metadata.findtext('n:metadata/n:id', namespaces=ns) == 'FullBleed.DotNet'
        assert metadata.findtext('n:metadata/n:version', namespaces=ns) == version
        assert metadata.findtext('n:metadata/n:license', namespaces=ns) == 'MIT'
        assert not metadata.findall('.//n:dependency', ns), 'Unexpected NuGet runtime dependency'
        assert 'lib/net8.0/FullBleed.DotNet.dll' in z.namelist()
        for file in ['LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md', 'native-provenance.json']:
            assert z.read(file) == (ROOT / file).read_bytes(), f'Stale {file} in package'
        for dependency in provenance['dependencies']:
            for license_file in dependency['license_files']:
                assert sha(z.read(license_file['path'])) == license_file['sha256']
        actual = [name for name in z.namelist() if name.startswith('runtimes/') and not name.endswith('/')]
        for rid, name in RIDS.items():
            path = f'runtimes/{rid}/native/{name}'
            if not args.all_rids and path not in actual:
                continue
            data = z.read(path)
            architecture(rid, data)
            staged = ROOT / path
            assert data == staged.read_bytes(), f'Package differs from staged {rid} library'
            assets.append({'rid': rid, 'path': path, 'bytes': len(data), 'sha256': sha(data)})
        assert assets and len(actual) == len(assets), 'Missing or unexpected native runtime files'
    manifest = {'schema': 'fullbleed.dotnet.package.v1', 'version': version,
                'source_commit': revision, 'engine_version': engine['version'],
                'engine_crate_sha256': engine['crate_sha256'], 'runtime_dependencies': [],
                'package': {'file': package.name, 'sha256': sha(package.read_bytes())},
                'symbols': {'file': symbols.name, 'sha256': sha(symbols.read_bytes())}, 'native_assets': assets}
    (packages / 'package-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    (packages / 'SHA256SUMS.txt').write_text(''.join(
        f'{sha(path.read_bytes())}  {path.name}\n' for path in [package, symbols]), encoding='utf-8')
    print(f'Verified FullBleed.DotNet {version}: {len(assets)} native runtimes; engine {engine["version"]}')


if __name__ == '__main__':
    main()
