"""Local launcher foundation. No downloads, lobby client or game networking."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LOCK = json.loads((ROOT / 'versions.lock.json').read_text())
ROM_SHA1 = LOCK['rom']['sha1']
ROM_SIZE = LOCK['rom']['size']

def read_rom(path, expected_sha1=ROM_SHA1):
    path = Path(path)
    if path.suffix.lower() == '.zip':
        with zipfile.ZipFile(path) as archive:
            if len(archive.infolist()) > 100:
                raise ValueError('Too many entries in ROM archive')
            matches = [x for x in archive.infolist() if not x.is_dir() and Path(x.filename).suffix.lower() == '.z64' and x.file_size == ROM_SIZE]
            if len(matches) != 1:
                raise ValueError('ZIP must contain exactly one 12 MiB .z64 ROM')
            with archive.open(matches[0]) as stream:
                data = stream.read(ROM_SIZE + 1)
    else:
        if path.stat().st_size != ROM_SIZE:
            raise ValueError('Expected a 12 MiB USA .z64 ROM')
        with path.open('rb') as stream:
            data = stream.read(ROM_SIZE + 1)
    if len(data) != ROM_SIZE or hashlib.sha1(data).hexdigest() != expected_sha1:
        raise ValueError('Unsupported ROM. Expected USA SHA1: ' + expected_sha1)
    return data

def inspect_engine(path, allow_development=False):
    path = Path(path).resolve()
    if path.name.lower() != 'projectmidas.exe' or not path.is_file():
        raise ValueError('Select ProjectMidas.exe from an installed engine or source build')
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    official = digest == LOCK['engine']['exe_sha256']
    if not official and not allow_development:
        raise ValueError('This is not the pinned release. Enable local source builds only for an executable you built or trust.')
    if not (path.parent / 'SDL2.dll').is_file():
        raise ValueError('SDL2.dll is missing beside the engine')
    return {'path': str(path), 'sha256': digest, 'official_preview': official}

def validate(engine, rom, allow_development=False):
    info = inspect_engine(engine, allow_development)
    read_rom(rom)
    return {'engine': info, 'rom_sha1': ROM_SHA1, 'rom_valid': True, 'online_multiplayer': False}

def launch(engine, rom, state_dir, allow_development=False):
    info = inspect_engine(engine, allow_development)
    data = read_rom(rom)
    rom = Path(rom).resolve()
    if rom.suffix.lower() == '.zip':
        # Never extract archive member names. Write a fixed, verified private file.
        cache = Path(state_dir) / 'rom-cache'
        cache.mkdir(parents=True, exist_ok=True)
        rom = cache / (ROM_SHA1 + '.z64')
        if not rom.exists() or hashlib.sha1(rom.read_bytes()).hexdigest() != ROM_SHA1:
            temporary = cache / (ROM_SHA1 + '.tmp')
            temporary.write_bytes(data)
            temporary.replace(rom)
    executable = Path(info['path'])
    return subprocess.Popen([str(executable), '--rom-file', str(rom)], cwd=executable.parent)

def gui(state_dir):
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
    state_dir = Path(state_dir)
    config_path = state_dir / 'launcher.json'
    settings = json.loads(config_path.read_text()) if config_path.exists() else {}
    workspace = ROOT.parent
    engine_default = workspace / 'Project-Midas/ProjectMidas.exe'
    rom_default = workspace / 'Project-Midas/Data/baserom.u.z64'
    window = tk.Tk()
    window.title('Canadaverse Launcher')
    window.geometry('720x310')
    window.minsize(620, 310)
    panel = ttk.Frame(window, padding=22)
    panel.pack(fill='both', expand=True)
    ttk.Label(panel, text='GoldenEye local play', font=('Segoe UI', 18, 'bold')).grid(row=0, column=0, columnspan=3, sticky='w', pady=(0,14))
    engine = tk.StringVar(value=settings.get('engine', str(engine_default) if engine_default.exists() else ''))
    rom = tk.StringVar(value=settings.get('rom', str(rom_default) if rom_default.exists() else ''))
    dev = tk.BooleanVar(value=False)
    status = tk.StringVar(value='Project Midas 1.0.2b preview. Online multiplayer is not implemented.')
    for row, label, value, types in [(1,'Engine',engine,[('Project Midas','ProjectMidas.exe')]),(2,'Your ROM or ZIP',rom,[('ROM backup','*.z64 *.zip')])]:
        ttk.Label(panel,text=label).grid(row=row,column=0,sticky='w',padx=(0,12),pady=6)
        ttk.Entry(panel,textvariable=value).grid(row=row,column=1,sticky='ew',pady=6)
        def browse(v=value, t=types):
            selected=filedialog.askopenfilename(filetypes=t)
            if selected: v.set(selected)
        ttk.Button(panel,text='Browse',command=browse).grid(row=row,column=2,padx=(8,0))
    ttk.Checkbutton(panel,text='Allow a local source build',variable=dev).grid(row=3,column=1,sticky='w',pady=8)
    def run(check_only):
        try:
            validate(engine.get(),rom.get(),dev.get())
            if not check_only:
                launch(engine.get(),rom.get(),state_dir,dev.get())
            state_dir.mkdir(parents=True,exist_ok=True)
            config_path.write_text(json.dumps({'engine':engine.get(),'rom':rom.get()},indent=2))
            status.set('ROM verified.' if check_only else 'Game started. Saves and graphics settings belong to the selected engine.')
        except (OSError,ValueError,zipfile.BadZipFile,RuntimeError) as error:
            messagebox.showerror('Cannot launch',str(error))
    buttons=ttk.Frame(panel)
    buttons.grid(row=4,column=1,sticky='w',pady=8)
    ttk.Button(buttons,text='Verify files',command=lambda:run(True)).pack(side='left',padx=(0,8))
    ttk.Button(buttons,text='Play',command=lambda:run(False)).pack(side='left')
    ttk.Label(panel,textvariable=status,wraplength=650).grid(row=5,column=0,columnspan=3,sticky='w',pady=12)
    panel.columnconfigure(1,weight=1)
    window.mainloop()

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--engine',type=Path)
    ap.add_argument('--rom',type=Path)
    ap.add_argument('--state-dir',type=Path,default=Path(os.environ.get('LOCALAPPDATA',Path.home()))/'Canadaverse007')
    ap.add_argument('--allow-development-build',action='store_true')
    mode=ap.add_mutually_exclusive_group()
    mode.add_argument('--check',action='store_true')
    mode.add_argument('--launch',action='store_true')
    args=ap.parse_args()
    if args.check or args.launch:
        if not args.engine or not args.rom: ap.error('--engine and --rom are required')
        try:
            if args.check: print(json.dumps(validate(args.engine,args.rom,args.allow_development_build),indent=2))
            else: print('Started PID',launch(args.engine,args.rom,args.state_dir,args.allow_development_build).pid)
        except (OSError,ValueError,zipfile.BadZipFile,RuntimeError) as error:
            ap.exit(1,str(error)+'\n')
    else: gui(args.state_dir)

if __name__ == '__main__': main()
