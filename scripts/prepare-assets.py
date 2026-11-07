"""Generate only the private ROM assets required by the pinned ROM-required build."""
import argparse
import csv
import hashlib
from pathlib import Path
import subprocess
import sys
import zlib

SHA1 = 'abe01e4aeb033b6c0836819f549c791b26cfde83'

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', required=True, type=Path)
    ap.add_argument('--rom', required=True, type=Path)
    args = ap.parse_args()
    root = args.source.resolve()
    rom = args.rom.read_bytes()
    if hashlib.sha1(rom).hexdigest() != SHA1:
        raise SystemExit('Unsupported ROM: expected GoldenEye USA SHA1 ' + SHA1)
    rows = list(csv.reader((root / 'scripts/filelist.u.csv').read_text().splitlines()))
    required = {'assets/ge007.u.2A4D50.usedby7F008DE4.bin'}
    required.update(r[2] for r in rows if r[2].startswith(('assets/music/', 'assets/ramrom/')) and r[4] == '1')
    props = ['legalpage', 'nintendologo', 'goldeneyelogo']
    required.update('assets/obseg/prop/P' + name + 'Z.bin' for name in props)
    for offset, size, name, compressed, *_ in rows:
        if name not in required:
            continue
        start, length = int(offset), int(size)
        data = rom[start:start + length]
        if len(data) != length:
            raise ValueError('ROM region out of bounds: ' + name)
        if compressed == '1':
            if data[:2] != b'\x11\x72':
                raise ValueError('Missing 1172 compression header: ' + name)
            data = zlib.decompress(data[2:], -15)
        path = (root / name).resolve()
        if not path.is_relative_to(root / 'assets'):
            raise ValueError('Asset path escapes source assets')
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists() or path.read_bytes() != data:
            path.write_bytes(data)
    end = next(r for r in rows if r[2] == 'assets/obseg/ob__ob_end.seg')
    start = int(end[0]) + int(end[1])
    image_bank = rom[start:start + 3075872]
    if len(image_bank) != 3075872:
        raise ValueError('Invalid image bank bounds')
    target = root / 'assets/images/combined/combined.bin'
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.exists() or target.read_bytes() != image_bank:
        target.write_bytes(image_bank)
    subprocess.run([sys.executable, '-X', 'utf8', 'scripts/generate_prop_model_c.py', *props], cwd=root, check=True)
    for prop in props:
        if not (root / 'assets/obseg/prop' / prop / 'Model.c').is_file():
            raise RuntimeError('Model generation failed: ' + prop)
    print('Private assets prepared; original ROM and ZIP unchanged.')

if __name__ == '__main__':
    main()
