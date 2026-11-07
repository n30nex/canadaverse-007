# Local licensing and distribution notes

This is a source/notice audit, not a legal clearance for publishing GoldenEye-derived material.

## Repository boundaries

The public repository contains our original launcher, room directory, build glue, compatibility patch, protocol design and documentation. Its MIT license covers those original contributions only. The private Project Midas engine checkout is a sibling directory, not a submodule or copied source tree in this repository. Keep its Git history out of the launcher history.

Project Midas's license grants MIT terms to its original port layer, scripts/tools and original source changes. It expressly excludes the decompiled GoldenEye code, ROM-derived assets, game names/trademarks and separately licensed third-party code. Its license also reserves the name Project Midas for the upstream project. Our compatibility patch retains the upstream notice in `licenses/Project-Midas.txt`; the launcher has its own name and does not claim endorsement.

The upstream tree itself contains reconstructed game code and data. Cloning it does not make those files MIT-licensed. In particular, `GE_ROMDATA_FALLBACK=OFF` is not a proof that the built executable contains no game data: `CMakeLists.txt` still keeps three menu/logo model objects and compiles `assets/oddtextures.c`, `assets/rarewarelogo.c` and `assets/font_dl.c`. These need a code/data audit and possibly ROM-only replacements before any engine-binary distribution decision. The release packages therefore contain **no engine executable**. The Windows package contains only our original launcher executable, its instructions and license.

## Dependencies

| Component | Observed notice / treatment |
| --- | --- |
| Original Midas port and our small CMake patch | MIT within upstream's stated scope; preserve attribution |
| GoldenEye decompilation / game data / branding | Not covered by that MIT grant; do not include in this package |
| Fast3D and GLAD/Khronos headers | Upstream third-party MIT/MIT-style notices must accompany a future engine package |
| SDL2 and SDL controller database | zlib notices in the official release; retain them if redistributed |
| Native compiler and MinGW runtimes | Development tools remain local; review GCC/MinGW runtime exception and winpthreads notices before shipping any added runtime DLL |
| gzip tools in the engine checkout | GPL-2.0-or-later notice; not used or included in this launcher package |
| Other historical build tools | Individual licenses apply; upstream notices identify some code with no published license. Do not bundle the tools directory wholesale. |

The authoritative current engine notices are in the installed release's `LICENSE.txt` and `THIRD_PARTY_NOTICES.txt`; the scoped license is also in the pinned source. Reference: [Project Midas repository](https://github.com/murkantor/Project-Midas/tree/9062f9f120b7f64d39e5db59500ed50e8e7a6686).

## Packaging policy

Players supply their own ROM. No ROM download feature, asset download mirror, extracted game files, saves, screenshots, proprietary sounds/music/models, game icon, credentials or service keys belong in this public package. Screenshots used for private verification remain outside this repository. ROM SHA1 values identify supported inputs; the launcher checks them locally and never uploads the file.

`.gitignore` excludes common private formats. `scripts/package.py` packages exact named source files, rejects executable/ROM signatures in source, audits reachable Git object paths and writes manifests/checksums. The separate Windows ZIP includes only the locally compiled original launcher plus its instructions/license; the Pi archive includes only service/deployment sources. This is stronger than copying a folder and hoping its ignore file is sufficient, but it does not replace review of new source/text content for accidentally pasted secrets or proprietary data. If a sensitive file ever enters history, merely adding it to `.gitignore` is insufficient; remove it from all reachable history and rotate any exposed credential before publishing.

Publication target: `n30nex/canadaverse-007`. Engine and game artifacts are excluded from all public outputs. The room directory is prepared for Pi deployment; public hosting is pending server access.
