# Canadaverse Launcher

**Your GoldenEye backup. A native Windows launcher. An open community room directory.**

Canadaverse launches an independently installed [Project Midas](https://github.com/murkantor/Project-Midas) engine, verifies your own USA ROM locally, and lets you see who's setting up a session. No ROM, game assets or engine executable is bundled.

**Preview 0.2.0-preview.1:** local play and staging-room discovery work. **Online gameplay does not.** Project Midas has no network game transport in the pinned version; a room listing cannot connect players to a match. Local single-player and split-screen remain available through the engine.

## Get started on Windows

1. Download `CanadaverseLauncher-0.2.0-preview.1-windows.zip` from [Releases](https://github.com/n30nex/canadaverse-007/releases).
2. Extract it and open `CanadaverseLauncher.exe`. Windows 10/11 with .NET Framework 4.8 is required; Python is not needed.
3. In **Setup**, choose your `ProjectMidas.exe`, or select **Install pinned engine** to download the official upstream preview. Both the archive and executable are checked against pinned SHA256 hashes. If the download cannot connect, download the [official Windows ZIP](https://github.com/murkantor/Project-Midas/releases/tag/1.0.2b) in your browser and choose **Install engine ZIP**.
4. Select your GoldenEye 007 USA `.z64` ROM or a ZIP containing it, then **Save setup**.
5. Choose **Verify files**, then **Play GoldenEye**.

Supported ROM: 12 MiB, SHA1 `abe01e4aeb033b6c0836819f549c791b26cfde83`. The original ZIP stays unchanged. Local settings and the verified ROM cache live in `%LOCALAPPDATA%\Canadaverse007`; engine saves stay beside the selected engine. Create a `portable` folder beside the executable, or use `--state-dir PATH`, for a portable profile.

The pinned engine is **Project Midas 1.0.2b preview**, commit `9062f9f120b7f64d39e5db59500ed50e8e7a6686`. It is deliberately identified as a preview. A newly installed engine gets native screen dimensions, widescreen, VSync, 4x MSAA, trilinear filtering, 16x anisotropy and a conservative 60 FPS cap. Existing engine settings are preserved. [Version lock](versions.lock.json) · [Graphics and modding](docs/GRAPHICS.md).

The launcher binary is not digitally signed. Release checksums detect accidental changes; they are not a publisher signature. Build it from the published source if desired.

## Rooms

**Rooms** lists host-created staging rooms, map, host presence and executable build identity. Hosts renew a 45-second lease every 15 seconds; closing the launcher removes the room or lets it expire. The host's chosen name, room name and build metadata are public. ROM contents, local paths and saves are never sent.

The default directory address is `https://lobbies.canadaverse.org`. **Deployment is pending server access; this address is not claimed to be live.** You can change the HTTPS origin in Setup or use a local development server. An unavailable directory does not block local play.

No fake player counts, demo rooms or working Join button are presented. Build filters are host-reported identity, not proof of a verified online simulation. The service explicitly reports `game_networking: false` and `joinable: false`.

## Build and test

```powershell
# Native launcher: uses the .NET Framework compiler included with Windows.
.\scripts\build-launcher.ps1 -Test

# Directory and tooling: Python 3.10+.
python -m pip install -r requirements-dev.txt
python -m unittest discover -s tests -v
python -m uvicorn server.app:app --host 127.0.0.1 --port 18707 --no-access-log
```

To test the native client's create/list/heartbeat/close flow against that server:

```powershell
.\dist\windows\LauncherTests.exe .local\native-tests http://127.0.0.1:18707
# Optional third argument: path to the official engine ZIP, to test installation.
```

Run `python scripts/package.py` after building to produce the Windows ZIP, complete source ZIP, Pi service archive, and SHA256 checksums. Packaging uses an exact allowlist and checks Git history paths; private files and engine artifacts are excluded.

## Develop and host

| Area | Entry point |
| --- | --- |
| Windows launcher | `launcher/windows/Program.cs`, `Core.cs` |
| Python CLI fallback | `launcher/launcher.py` |
| Room directory and browser page | `server/app.py`, `server/static/` |
| Pi service and Cloudflare routing | [Deployment guide](docs/DEPLOYMENT.md), `deploy/` |
| Implemented room API | [Room API](protocol/room-api.md) |
| Future engine networking | [Multiplayer plan](docs/MULTIPLAYER.md) |
| Editable engine source/build | [Engine build notes](docs/BUILD.md) |
| Licensing and package boundaries | [Licensing](docs/LICENSING.md) |

This project is independent of Project Midas, Nintendo, Rare and the James Bond rights holders. MIT covers our original launcher, service and tooling only. It does not grant rights to the game, its assets, engine source or trademarks.
