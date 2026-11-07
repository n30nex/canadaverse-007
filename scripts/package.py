"""Release explicit allowlisted sources, standalone launcher and Pi service."""
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = json.loads((ROOT / 'versions.lock.json').read_text())['launcher_version']
FILES = {
    '.gitignore', '.gitattributes', 'LICENSE', 'README.md', 'versions.lock.json', 'requirements-dev.txt',
    'launcher/launcher.py', 'launcher/windows/Program.cs', 'launcher/windows/Core.cs', 'launcher/windows/app.manifest',
    'protocol/compatibility.py', 'protocol/lobby-api.md', 'protocol/room-api.md',
    'scripts/build.ps1', 'scripts/build-launcher.ps1', 'scripts/prepare-assets.py', 'scripts/smoke-test.ps1', 'scripts/package.py',
    'cmake/native-mingw32.cmake', 'patches/windows-gcc14.patch',
    'docs/BUILD.md', 'docs/GRAPHICS.md', 'docs/MULTIPLAYER.md', 'docs/LICENSING.md', 'docs/STATUS.md',
    'docs/DEPLOYMENT.md', 'docs/WINDOWS.txt',
    'licenses/Project-Midas.txt', 'tests/test_foundation.py', 'tests/test_directory.py', 'tests/LauncherTests.cs',
    'server/__init__.py', 'server/app.py', 'server/requirements.txt', 'server/static/index.html', 'server/static/rooms.js', 'server/static/style.css',
    'deploy/canadaverse-007.service', 'deploy/install-pi.sh',
}


def approved(path):
    return Path(path).as_posix() in FILES


def audit_history():
    if not (ROOT / '.git').exists():
        return
    result = subprocess.run(['git', '-C', str(ROOT), 'rev-list', '--objects', '--all'], capture_output=True, text=True, check=True)
    named = [(line.partition(' ')[0], line.partition(' ')[2]) for line in result.stdout.splitlines() if ' ' in line]
    types = subprocess.run(['git', '-C', str(ROOT), 'cat-file', '--batch-check=%(objecttype)'], input=''.join(oid + '\n' for oid, _ in named), capture_output=True, text=True, check=True).stdout.splitlines()
    bad = [name for (_, name), kind in zip(named, types) if kind == 'blob' and not approved(name)]
    if bad:
        raise SystemExit('Excluded paths in Git history: ' + ', '.join(bad[:10]))


def source_files():
    result = {}
    for name in sorted(FILES):
        path = ROOT / name
        if not path.is_file() or path.is_symlink():
            raise SystemExit('Missing or symlinked source: ' + name)
        data = path.read_bytes()
        data.decode('utf-8')
        if data.startswith((b'MZ', b'\x80\x37\x12\x40', b'\x37\x80\x40\x12', b'\x40\x12\x37\x80')):
            raise SystemExit('Executable or ROM signature in ' + name)
        if re.search(rb'-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----|\bghp_[A-Za-z0-9]{30,}|\bgithub_pat_[A-Za-z0-9_]{30,}', data):
            raise SystemExit('Possible credential in ' + name)
        result[name] = data
    return result


def write_zip(path, files):
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(2026, 11, 7, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)


def main():
    audit_history()
    sources = source_files()
    out = ROOT / 'dist'
    out.mkdir(exist_ok=True)
    source = out / ('canadaverse-007-' + VERSION + '-source.zip')
    write_zip(source, sources)
    exe = out / 'windows/CanadaverseLauncher.exe'
    if not exe.is_file() or not exe.read_bytes().startswith(b'MZ'):
        raise SystemExit('Build the Windows launcher before packaging.')
    windows = out / ('CanadaverseLauncher-' + VERSION + '-windows.zip')
    write_zip(windows, {'CanadaverseLauncher.exe': exe.read_bytes(), 'README.txt': sources['docs/WINDOWS.txt'], 'LICENSE.txt': sources['LICENSE']})
    pi = out / ('canadaverse-007-' + VERSION + '-pi.tar.gz')
    with pi.open('wb') as raw, gzip.GzipFile(fileobj=raw, mode='wb', filename='', mtime=0) as compressed, tarfile.open(fileobj=compressed, mode='w') as archive:
        for name, data in sources.items():
            if not (name.startswith(('server/', 'deploy/')) or name in {'LICENSE', 'docs/DEPLOYMENT.md', 'protocol/room-api.md'}):
                continue
            info = tarfile.TarInfo(name)
            info.size = len(data)
            info.mode = 0o755 if name.endswith('.sh') else 0o644
            info.mtime = 1794009600
            archive.addfile(info, io.BytesIO(data))
    artifacts = [source, windows, pi]
    sums = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n' for p in artifacts)
    (out / 'SHA256SUMS.txt').write_text(sums, encoding='utf-8')
    (out / 'package-manifest.json').write_text(json.dumps({name: hashlib.sha256(data).hexdigest() for name, data in sources.items()}, indent=2), encoding='utf-8')
    print(json.dumps({'artifacts': [str(p) for p in artifacts], 'source_files': len(sources)}, indent=2))


if __name__ == '__main__':
    main()
