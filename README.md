# Darkmount Hub

Live PC stats, animations and alerts on the **be quiet! Dark Mount** keyboard's media dock screen. It runs as a small tray app and replaces IO Center's dock screen while it runs.

![Stats screen](docs/images/stats-nogame.png) ![In game](docs/images/stats-game.png)

## Features

- **Stats screen:**
  - CPU and GPU temperature, shown largest, with wave graphs
  - CPU and GPU power (W) and load (%)
  - RAM and VRAM usage bars
- **FPS row in games:** appears automatically. It shows average FPS, the 0.1% (or 1%) low, and an FPS graph.
- **Animation screen:**
  - Built-in themes: Plasma, Matrix rain, Starfield
  - Your own GIF or video, or a folder of pictures
  - Plays as an ambient slideshow, because the dock can only redraw about every 2 seconds (see *Hardware limits*)
- **Auto switching:** stats while a game runs, your default screen otherwise. The hotkey (**Ctrl+Alt+Shift+D**) or the tray menu switches screens manually.
- **Alerts:** a red banner for a hot CPU or GPU, RAM or VRAM nearly full, or FPS dropping in a game. The stats move down so nothing is hidden. Thresholds are set in Settings.
- **Dock basics without IO Center:** the app sets the dock clock and keeps the screen awake while it runs. It restores your own dock settings when it exits or pauses.
- Tray menu, settings window with a live preview, and an option to start with Windows.

## Requirements

- Windows 10 or 11, and a be quiet! Dark Mount with the media dock attached.
- **MSI Afterburner** running. It provides CPU and GPU temperature, power and load, RAM, VRAM and FPS.
  - For the "0.1% low" value, enable *Framerate 0.1% low* under Afterburner → Settings → Monitoring. Otherwise the 1% low is shown.
- **RivaTuner Statistics Server** running (installed with Afterburner). It detects which game is running.
- Optional: **HWiNFO** with *Shared Memory Support* enabled. Its values are preferred when available.

## Install and run

1. Download or build `DarkmountHub.exe` (a single self-contained file) and put it anywhere, for example `C:\Tools\DarkmountHub`.
2. **Close IO Center** (Exit from its tray icon). Only one app can drive the dock at a time. If IO Center starts, Darkmount Hub pauses automatically and hands the dock back.
3. Run `DarkmountHub.exe`. A tray icon appears.
4. If the dock screen is dark, **press a dock button once**. The dock only accepts images while its screen is awake, and the app keeps it awake from then on.

- **Exit:** use the tray menu, or run `DarkmountHub.exe --exit`. Either way your dock settings are restored.
- **Files:**
  - Settings: `%APPDATA%\DarkmountHub\settings.json`
  - Logs: `%LOCALAPPDATA%\DarkmountHub\logs`

## Hardware limits (measured on real hardware)

- The dock shows 320×240 RGB565 images only. JPEG, PNG and GIF are decoded on the PC.
- The dock redraws only after a **complete** image upload, which takes about 1.6 s. That is why the screens refresh about every 2 s.
- Images live in the keyboard's RAM, never in flash, so constant updates cause no wear. Unplugging the keyboard resets everything.
- The firmware sometimes holds back a reply until more data arrives. The app sends a short repeat of the same write to release it. It never sends pixel data twice.

## Safety

- The app can only send an allowlisted set of commands. The code has no way to send firmware update, factory reset, serial number, raw storage or calibration commands.
- It never talks to a keyboard in bootloader mode (PID 0x0009).
- Before changing any setting, it backs up your original dock settings to `%APPDATA%\DarkmountHub\dock-config-backup.hex`, and it always restores from that backup.

## Build

```
dotnet test tests/Darkmount.Tests
dotnet publish src/Darkmount.App/Darkmount.App.csproj -c Release -o publish --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

## Project layout

| Path | Purpose |
|---|---|
| `src/Darkmount.QLink` | USB protocol (QLink): framing, CRC, sessions, allowlist, media dock commands |
| `src/Darkmount.Dock` | Connection lifecycle, IO Center back-off, config backup, frame uploads |
| `src/Darkmount.Sensors` | Afterburner, HWiNFO and RivaTuner readers, game detection |
| `src/Darkmount.Screens` | 320×240 rendering (SkiaSharp): stats, animations, alert banner |
| `src/Darkmount.App` | Tray app, frame pipeline, alerts, auto switching, settings |
| `tools/DockBench` | Upload benchmark against the real keyboard |
| `probe/` | Original reverse-engineering and hardware test tool |
| `docs/` | Protocol notes, design spec and plan |

*Not affiliated with be quiet!. Use at your own risk.*
