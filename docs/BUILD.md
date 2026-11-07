# Native Windows build

## Pinned inputs

Use the hashes and download URLs in `versions.lock.json`. The tested toolchain is Winlibs GCC 14.2.0, **i686 / POSIX / DWARF / MSVCRT**, MinGW-w64 12.0.0 r3; SDL2 2.30.11's **i686-w64-mingw32** development package; GNU Make 4.4.1; CMake 3.31.6-msvc6; Python 3.13; Git for Windows. A 64-bit-only GCC installation cannot build this pointer-dependent engine correctly.

Local tool paths:

- `F:\007\toolchains\mingw32\bin`
- `F:\007\toolchains\SDL2-2.30.11\i686-w64-mingw32`
- `D:\VS2022BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe`

On another PC, download those exact archives from their official release URLs, verify the lock hashes, and extract under `toolchains`. Extract the Winlibs 7z with a 7z-capable archive program; this machine's Windows `tar` reported `LZMA codec unsupported`. The archives are already downloaded and extracted on this PC. Pass `-CMake` if CMake is installed elsewhere.

## Source provenance

The private source checkout at `F:\007\source\Project-Midas` has upstream HEAD `9062f9f120b7f64d39e5db59500ed50e8e7a6686`, tag `1.0.2b`, and local branch `local-windows`. The commit object and complete tree were checked against GitHub hashes. All source code files are present. The Windows compatibility patch is the sole tracked modification.

This is a **shallow partial checkout reconstructed from the GitHub API**, not a successful ordinary network clone. The managed shell's `git clone` failed with `Failed to connect to 127.0.0.1 port 9`; browser source-archive downloads also failed. Of 2,248 upstream blobs, 2,247 are present and verified. The unused historical binary `tools/GELauncher.exe` became unreadable (`OSError: [Errno 22] Invalid argument`) and disappeared after acquisition. It is deliberately omitted; the current engine, build and our launcher do not need it. No protection was disabled to recover it. The missing object is marked as promised by origin and excluded from the sparse working tree.

Legacy Japanese text files could not be retrieved faithfully as text through the connector. Their string bytes were regenerated privately from the supplied ROM and accepted only when their complete Git blob hash exactly matched upstream. They are part of upstream's existing tree, not newly committed extracted assets. Provenance manifests and recovery scripts are under `F:\007\local\source-provenance` and `F:\007\local` and are not in the public package.

To obtain a conventional clone/full history on a machine with working GitHub Git access:

```powershell
git clone --branch 1.0.2b https://github.com/murkantor/Project-Midas.git Project-Midas
git -C Project-Midas rev-parse HEAD
```

The result must equal the pinned commit. Keep this checkout private and outside the launcher repository. A fresh clone is preferable to fetching the omitted, unused historical executable into this local checkout.

## Build commands

```powershell
& F:\007\canadaverse-007\scripts\build.ps1
# Full rebuild, using the same output directory and pinned inputs:
& F:\007\canadaverse-007\scripts\build.ps1 -Clean
```

`-Workspace`, `-Source`, `-Build`, `-Rom`, `-CMake` and `-Jobs` override paths or parallelism. The script rejects an unexpected upstream HEAD and applies the versioned patch only when needed. It verifies the ROM, generates only the private build dependencies, configures `GE_VIDEO=ON`, `GE_ROMDATA_FALLBACK=OFF`, `GE_UNLOCK_ALL=OFF`, and builds `ge` using MinGW Makefiles. CMake's Ninja compiler probe stalled on this machine; GNU Make completed it and is the tested generator.

The compatibility patch returns three legacy C diagnostics to warnings for GCC 14 (pointer types, implicit declarations, integer conversions), including the separate asset compiler invocation. This preserves upstream's GCC-13-era behavior rather than claiming to fix all the decompilation's C type issues. Windows API declarations target Windows 7 or later so `CreateWaitableTimerExW` gets its correct prototype/calling convention. Without that target the first link failed with `undefined reference to CreateWaitableTimerExW`.

The build script copies the **32-bit `libwinpthread-1.dll`** beside the executable as well as CMake's SDL2.dll. Without it, Windows found an incompatible runtime and the first smoke test exited `-1073741701` (`0xC000007B`). The corrected runtime passed the mission test. Keep both DLLs with any private build.

`SOURCE_DATE_EPOCH=1792748901` fixes the build date/time to the upstream commit and `--no-insert-timestamp` removes the changing PE link timestamp. Reproducibility is tested on this machine at the same absolute paths; cross-machine byte identity is not promised because debug information embeds paths. Compiler and dependency upgrades require another validation run.

## Run the build

```powershell
python F:\007\canadaverse-007\launcher\launcher.py --engine F:\007\build\Project-Midas-make\ProjectMidas.exe --rom F:\007\Project-Midas\Data\baserom.u.z64 --allow-development-build --launch --state-dir F:\007\local\launcher
```

The source build has its own save/settings location. Copying a graphics INI is safe; do not automatically overwrite saves between builds.

## Re-run verification

```powershell
& F:\007\canadaverse-007\scripts\smoke-test.ps1 -Engine F:\007\build\Project-Midas-make -Mode mission -Output F:\007\local\verification\source-dam -Frames 4200
```

The test uses upstream's own `GE_STAGE`, `GE_SKIPINTRO`, `GE_DRIVE`, `GE_AUTOFIRE`, `GE_INPUTREC`, `GE_SHOT`, and `GE_FRAMES` harness. It boots Dam, injects movement/fire, records the game-facing input/position stream, saves renderer captures and exits. `GE_NOINISAVE` prevents test progress/settings from becoming normal saves. Inspect `result.json`, both logs and the captures: exit code alone is not sufficient evidence of rendered gameplay. This is an automated smoke test, not a completed mission or a manual controller test.
