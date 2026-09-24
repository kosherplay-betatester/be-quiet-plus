# Darkmount Hub — Phase 1 design

Status: approved in conversation 2026-09-24 (user added: CPU and GPU temperatures must be prominent).
Scope: phase 1 of 3. Phase 2 (lighting, key bindings, macros, display keys, profiles) and
phase 3 (now-playing, per-game profiles, other desktop-only extras) get their own specs.

## 1. Goal

A Windows tray app that drives the be quiet! Dark Mount media dock screen (320×240) with live PC
information, replacing IO Center's dock role while it runs.

Features:

- **Stats matrix** screen: CPU and GPU temperature (large, with temperature wave graph), power (W) and
  load (%); RAM and VRAM used/total; FPS row with average FPS, 0.1 % low and FPS wave graph, added
  automatically while a game runs.
- **Animation** screen: user GIF/video or built-in themes, shown as an ambient slideshow (see §3).
- **Auto switching**: game running → stats with FPS row; otherwise the user's default screen.
- **Alerts overlay**: red banner over any screen when a threshold is crossed; triggers an immediate refresh.
- **Dock basics without IO Center**: set the dock clock, keep the screen awake while running, restore the
  user's dock settings on exit.
- Tray icon, settings window, global hotkey, start with Windows.

## 2. Verified hardware facts (constraints)

From tests on the user's keyboard (firmware Main MCU 1.29.0, QLink 1.0.1). Protocol: `docs/QLINK_PROTOCOL.md`.

| Fact | Consequence |
|---|---|
| Screensaver image slot (media dock feature 0x21, SetImage type 0) is 320×240 RGB565 LE, RAM only | Constant updates cause no flash wear; replug resets it |
| Only 320×240 headers accepted; JPEG/PNG/WebP are drawn as raw bytes | App decodes everything on the PC and sends raw RGB565 |
| Dock redraws only after one complete upload (header, then all 153,600 bytes) | Every refresh is a full frame |
| Full upload ≈ 1.6 s (header ≈ 0.73 s, pixels ≈ 0.9 s using 4,000-byte multi-frame requests) | Refresh cadence ≈ 2 s |
| Device sometimes stalls 1.5–3 s (screen transitions, user touching the dock) | A stall mid-upload spoils the image (dock shows the logo) → restart the whole upload from the header |
| Screensaver must be enabled **after** a complete upload | Upload first, then SetConfig |
| Config and image writes do not wake a dock whose screen is off | Keep screen-off timeout at "never" (0) while running |
| Dock clock needs the host to send the time | Send SetDateTime on connect and hourly |
| Only one Active session; web client auto-yields | App backs off while IO Center is running |

## 3. Refresh model

- One **frame pipeline**: sample sensors → render active screen + overlay → compare with last uploaded
  frame → upload if changed.
- Target cadence 2.0 s (next frame starts when the previous upload finishes, never earlier than 2.0 s after
  the previous start). An alert that newly fires skips the wait.
- Upload sequence per frame: header (9 bytes) → pixel data in 4,000-byte multi-frame requests, strictly
  sequential. On any timeout: wait until the device answers `MediaDock_GetState`, then **restart the frame
  from the header**. Max 3 restarts per frame, then drop the frame and continue with the next one.
- Animation screen advances one source frame per upload (≈0.5 fps). The UI labels it "ambient".

## 4. Architecture

Solution `DarkmountHub.sln`, .NET 10, C#.

| Project | Responsibility | Depends on |
|---|---|---|
| `Darkmount.QLink` | HID transport (HidSharp), framing, CRC-16/MODBUS, request/response with request-id matching, notifications, session open/keep-alive/close, command allowlist, bootloader refusal, typed Media Dock API (GetState, Get/SetConfig, SetDateTime, SetImage) | HidSharp |
| `Darkmount.Dock` | `DockConnection` (find device, connect, reconnect on unplug, IO Center back-off), `FrameUploader` (§3), `DockConfigGuard` (backup and restore of the user's dock config) | QLink |
| `Darkmount.Sensors` | `HwInfoSource` (HWiNFO shared memory), `AfterburnerSource` (MAHM shared memory), `RtssSource` (RTSS shared memory, game/FPS detection), `SensorHub` merging them into one `Snapshot` | — |
| `Darkmount.Screens` | `IDockScreen.Render(SKCanvas, ScreenContext)`: `StatsScreen`, `AnimationScreen`, `AlertOverlay`, shared `Theme` and `WaveGraph`; `History` ring buffers (60 samples = 2 min) | SkiaSharp, Sensors (types only) |
| `Darkmount.App` | WinForms tray app: tray menu, settings window, global hotkey, autostart (HKCU Run), single instance, `settings.json`, logging, `AutoSwitcher`, `AlertEngine`, frame pipeline host | all above |
| `Darkmount.Tests` | xUnit tests (§8) | all above |

The existing `probe/` stays as a developer tool.

### 4.1 Connection lifecycle

1. Poll for VID 0x373F: PID 0x0001 interface MI_02 / usage page 0xFF00 every 2 s. PID 0x0009 (bootloader) → never open.
2. If an IO Center process (`IO_Center`) is running → stay disconnected and show "Paused — IO Center is running" in the tray.
3. Open session as client type 2 (Web). If not Active → RequestStateChange(Active), wait ≤ 2 s; on failure retry later.
4. On connect: read and back up the dock config (backup file is written only once and only if the config is
   not a test/app config: idle delay ≥ 5 s and screen-off ≠ 0; otherwise use the known original
   `DC4D0001021E003C00`); SetDateTime; upload the first frame; then SetConfig (screensaver = image,
   idle delay = user setting, default 3 s; screen off = 0).
5. Keep-alive every 1 s whenever no upload is in progress.
6. On exit, IO Center start, or tray "Pause": restore the backed-up config, close the session.
7. HID read/write errors → treat as unplug → back to step 1.

## 5. Sensors

- **HWiNFO** (primary for CPU/GPU/RAM): shared memory `Global\HWiNFO_SENS_SM2`. Readings are matched by
  label, configurable in settings. Defaults:
  - CPU temp: `CPU Package`, `CPU (Tctl/Tdie)`
  - CPU power: `CPU Package Power`
  - CPU load: `Total CPU Usage`
  - GPU temp: `GPU Temperature`
  - GPU power: `GPU Power`, `Total Board Power`
  - GPU load: `GPU Core Load`
  - RAM: `Physical Memory Used`, `Physical Memory Available`
  - VRAM: `GPU Memory Allocated`, `GPU D3D Memory Dedicated`
- **Afterburner MAHM** (`MAHMSharedMemory`): fallback for all of the above, and the primary source for
  `Framerate`, `Framerate 0.1% low`.
- **RTSS** (`RTSSSharedMemoryV2`): game detection = the foreground process has an RTSS app entry with a
  framerate > 0 updated in the last 2 s, and is not on the exclude list (default: browsers, explorer,
  Discord, the app itself). FPS fallback = RTSS framerate; 0.1 % low fallback = computed from the RTSS
  frame-time buffer if present, otherwise shown as "--".
- RAM total fallback: `GlobalMemoryStatusEx`.
- A missing source never throws: its values are `null`, rendered as "--", and the tray tooltip names the
  missing source (for example "Enable Shared Memory Support in HWiNFO").
- Sampling: every frame (≈2 s); graphs keep 60 samples.

## 6. Screens (320×240)

Dark theme (near-black background, one accent colour per group: CPU orange, GPU green, MEM blue, FPS
purple), Segoe UI, anti-aliased.

**Stats matrix, no game** — three rows of 80 px:

- CPU row: label, **temperature large (e.g. "72°C")**, then load % and power W; right half: wave graph
  with the temperature line on top of a filled load area.
- GPU row: same layout as the CPU row.
- MEM row: RAM bar "18.2 / 32 GB", VRAM bar "6.1 / 12 GB".

**Stats matrix, game running** — CPU 60 px, GPU 60 px, MEM 40 px (both bars compact), FPS 80 px: average
FPS large, "0.1% low" value, FPS wave graph with the 0.1 % low as a dashed line.

**Animation**: current frame scaled to fill 320×240 (cover); source = GIF, MP4 (decoded via Windows
Media Foundation), a folder of images, or built-in themes (Plasma, Matrix rain, Starfield) rendered by Skia.

**Alert overlay**: 36 px red banner at the top with an icon and text (e.g. "GPU 91°C"); shows while the
condition holds plus 10 s.

## 7. Behaviour and settings

- **Auto switching**: game detected → stats (with FPS row); game ends → user default screen. A manual
  choice holds until the next game start/stop.
- **Alerts** (defaults, editable): CPU temp ≥ 90 °C, GPU temp ≥ 85 °C, RAM ≥ 90 %, VRAM ≥ 95 %, in game
  FPS < 30 for ≥ 4 s. Each alert can be switched off.
- **Hotkey**: Ctrl+Alt+Shift+D cycles screens (editable).
- **Tray menu**: Stats / Animation / Auto (default), Pause dock, Settings…, Start with Windows ✓, Exit.
- **Settings window**: default screen, animation source, alert thresholds, sensor label overrides, dock
  idle delay, hotkey, autostart.
- Settings file: `%APPDATA%\DarkmountHub\settings.json`. Logs: `%LOCALAPPDATA%\DarkmountHub\logs`
  (daily, 7 kept).

## 8. Testing

- Unit: CRC check value 0x4B37; frame build/parse round-trips (single and multi-frame); request-id
  matching skips stale replies; allowlist rejects DFU/factory reset.
- `FrameUploader` against a fake transport that injects stalls → verify restart-from-header, max restarts,
  no overlapping uploads, config written only after the first complete frame.
- Sensor parsing against captured byte blobs of HWiNFO/MAHM/RTSS shared memory layouts.
- Rendering: each screen rendered to PNG with fixed snapshots (null values, game/no game, alert) for visual review.
- Hardware acceptance with the user watching: stats refresh visibly every ≈2 s, FPS row appears in a game,
  alert banner appears (forced by a test threshold), IO Center start pauses the app, exit restores the dock.

## 9. Out of scope (phase 1)

Lighting, key bindings, macros, display-key images, profiles (phase 2); now-playing, per-game profiles,
playback info (phase 3); smooth animation (hardware limit ≈0.5 fps).
