# Verified local status — 2026-11-07

## Windows launcher and room directory release

Version `0.2.0-preview.1` adds a standalone .NET Framework Windows launcher and a working FastAPI/SQLite staging-room directory. The original Python CLI remains available. Release packages contain original launcher/service code and the launcher executable only; they exclude the engine and game data.

- Native tests: 18 assertions for ROM/archive validation, executable identity, settings, origins, body limits and real JSON room-list decoding. A real HTTP integration run passes 23 assertions including create, list, heartbeat, removal and installation from the verified official Windows ZIP.
- Python: 15 tests passed for leases, expiry, forged tokens, strict/oversized/chunked inputs, rate limits, database reopen, Cloudflare mode, HTTP/static responses, ROM handling, compatibility and package exclusions.
- Actual Windows UI: opened successfully, verified this user's ROM and engine, loaded Rooms, created a real staging room, renewed its lease beyond 45 seconds, and removed it when the launcher closed. The same room was observed in the separate browser page. It was removed after testing.
- Official engine ZIP installation and hash verification passed. The automatic HTTPS-download test was blocked by this session's Windows security context (`No credentials are available in the security package`). No TLS validation was disabled. The launcher now also accepts the official ZIP through **Install engine ZIP**; the download path remains unverified in this session.
- The browser page was visually inspected. Native screenshot capture returned `FrameArrived timed out` and `window capture timed out`; Windows UI checks used the accessibility tree and keyboard instead. A pixel-level native layout review is therefore incomplete.
- Pi/systemd deployment is prepared but not executed: this session's SSH command resolves to a disabled wrapper. No production directory, Cloudflare route, game netcode or playable Internet match is claimed.

The local API listens at `127.0.0.1:18707` during verification. The public hostname remains a deployment target. See `docs/DEPLOYMENT.md` and `protocol/room-api.md` for the implemented service and required deployment verification.

## Original engine setup verification

- Installed official Project Midas **1.0.2b preview** at `F:\007\Project-Midas`. The release list was checked again on November 7: v1.0.1 is the latest stable; 1.0.2b is the latest preview. The preview includes relevant high-frame-rate and Dam fixes.
- Preserved `F:\007\GoldenEye 007 (1).zip`. Its SHA256 remains `813516a66bb596d5c2a901b9a410ca52a04937e56f938ffa7429fa583cce5dec`. The installed ROM SHA1 matches the supplied `abe01e4aeb033b6c0836819f549c791b26cfde83`.
- Configured the GTX 1060 6GB / Acer XF240H for native 1920×1080, widescreen, 144 FPS cap, VSync, 4× MSAA, trilinear filtering and 16× anisotropy. Normal progression and gameplay options remain enabled; cheats are off.
- Created `Play-GoldenEye-007.lnk`, `Play-GoldenEye-Source.lnk` and `Canadaverse-Launcher.lnk` in `F:\007`.
- Verified rendered intro/menu response with injected Start input. The release reached the mission-selection folder; renderer captures and an input recording were inspected. This verifies the game-facing input path, not a separate manual keyboard/controller test.
- Ran **Dam** in both the official release and our compiled executable. Automated input moved Bond and fired the pistol; inspected images showed the level, PP7, ammunition changes and bullet effects. Both 4,200-frame harness runs exited with code 0. Neither run completed the mission objectives.
- Built the pinned source natively on Windows using GCC 14.2.0 i686, SDL2 2.30.11 and GNU Make. A clean rebuild produced **identical executable SHA256** `512611d0fa9da40f07c98e86cab2bd4269978911f25e259d3bf988d80a72060d`. This result applies to the same toolchain and paths on this PC.
- Verified the launcher CLI against the actual ZIP and official engine, including a real launch through `--rom-file` using its SHA1-verified private cache. That launch reached its 360-frame limit normally. Four synthetic unit tests passed for ROM/hash/archive handling, simulation compatibility and package exclusions. The optional Tk interface itself has not had a manual UI test.

## Evidence kept privately

| Evidence | Location |
| --- | --- |
| Release title/menu captures, input and exit result | `F:\007\local\verification\release-title` |
| Release Dam captures, input/position record, logs and exit result | `F:\007\local\verification\release-dam` |
| Built-source Dam equivalent | `F:\007\local\verification\source-dam` |
| Convenient PNG images | `F:\007\local\verification\title-menu.png`, `dam-release.png`, `dam-source.png` |
| Successful build, rebuild and clean rebuild logs | `F:\007\logs\source-build.log`, `source-rebuild.log`, `source-clean-rebuild.log` |
| Clean-build hash comparison | `F:\007\local\reproducibility.json` |
| Exact upstream tree/commit provenance | `F:\007\local\source-provenance` |

## Practical limits

This is a preview and a short smoke test, not an all-mission regression pass. The release log sampled around 142–144 rendered FPS during Dam, with loading/capture and frame-time spikes. No claim of a universally locked frame rate, perfect original-game parity, or subjective audio quality is made. A controller was detected during setup, but physical controller play was not verified.

The source repository is a SHA-verified shallow partial checkout. All source code is present; one unused historical executable (`tools/GELauncher.exe`) and full upstream history are absent. See BUILD.md for the exact acquisition errors and how to obtain a conventional clone when normal GitHub Git access is available. This does not block editing or rebuilding the game.

New lighting effects, true HD textures, model replacements, game networking, game-ready lobbies and relay services are **not implemented**. The new staging directory described above is implemented locally. The graphics code map and multiplayer plan identify the required work. The public foundation contains original launcher/tooling/protocol work only; it has no ROMs, extracted assets, saves, credentials or engine binary. Engine redistribution is excluded. Public service deployment remains pending server access.
