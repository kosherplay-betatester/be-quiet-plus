# Darkmount Hub Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tray app that shows live PC stats, animations and alerts on the Dark Mount media dock (spec: `docs/superpowers/specs/2026-09-24-darkmount-hub-phase1-design.md`).

**Architecture:** Five libraries plus a WinForms tray host. `QLink` speaks the USB protocol; `Dock` owns the connection, config backup and full-frame uploads; `Sensors` reads Afterburner/HWiNFO/RTSS shared memory; `Screens` renders 320×240 frames with SkiaSharp; `App` wires a 2-second frame pipeline, auto switching, alerts, tray, settings and autostart.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0` for App), C#, HidSharp 2.1.0, SkiaSharp, xUnit.

**Verified environment facts (2026-09-24, user PC):**
- Afterburner MAHM v2.0: header 32 bytes, entry 1324 bytes; name @0 (260), units @260, data float @1300 (FLT_MAX = no data), dwGpu @1316. Names seen: `GPU2 temperature`, `GPU2 usage`, `GPU2 memory usage` (MB), `GPU2 power` (W), `CPU temperature`, `CPU usage`, `CPU power`, `RAM usage` (MB), `Framerate`, `Framerate 1% Low`. `GPUn` prefix appears because two adapters exist (AMD iGPU + RTX 5070 Ti 16 GB).
- RTSS v2.15: app array @ header+12 offset, entry 12416 bytes; pid @0, name @4 (260), dwTime0 @268, dwTime1 @272, dwFrames @276, dwFrameTime µs @280. fps = 1000·frames/(t1−t0).
- HWiNFO shared memory `Global\HWiNFO_SENS_SM2` not enabled (optional source; readings: label @12, unit @268, value double @284, sensor index @4; sensor name @8 in sensor element).
- VRAM total: registry `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000N\HardwareInformation.qwMemorySize`. RAM total: `GlobalMemoryStatusEx`.

---

## File structure

```
DarkmountHub.sln
nuget.config
src/Darkmount.QLink/     Crc16.cs Frame.cs Commands.cs IHidTransport.cs HidSharpTransport.cs QLinkClient.cs QLinkException.cs MediaDock.cs DockConfig.cs
src/Darkmount.Dock/      Rgb565.cs DockConfigGuard.cs FrameUploader.cs DockConnection.cs IoCenterDetector.cs
src/Darkmount.Sensors/   Snapshot.cs SharedMemory.cs MahmReader.cs HwInfoReader.cs RtssReader.cs SystemInfo.cs SensorHub.cs
src/Darkmount.Screens/   Theme.cs History.cs WaveGraph.cs ScreenContext.cs IDockScreen.cs StatsScreen.cs AlertOverlay.cs
                         Animation/IFrameSource.cs GifSource.cs FolderSource.cs VideoSource.cs ThemeSources.cs AnimationScreen.cs
src/Darkmount.App/       Program.cs AppSettings.cs SettingsStore.cs Log.cs FramePipeline.cs AutoSwitcher.cs AlertEngine.cs
                         HotkeyWindow.cs Autostart.cs TrayApp.cs SettingsForm.cs
tests/Darkmount.Tests/   QLinkTests.cs FakeTransport.cs FrameUploaderTests.cs SensorParsingTests.cs ScreenRenderTests.cs AlertEngineTests.cs AutoSwitcherTests.cs
```

## Tasks

### Task 1: Solution scaffold
- [ ] Create solution, six projects, project references, packages (HidSharp → QLink; SkiaSharp → Screens, App; xUnit → Tests), `nuget.config` with nuget.org.
- [ ] `dotnet build` → succeeds. Commit `chore: scaffold DarkmountHub solution`.

### Task 2: QLink framing and CRC (TDD)
Tests (`QLinkTests.cs`): `Crc16("123456789") == 0x4B37`; single-frame build → byte layout `[len=data+6][0][sid][0][req][feat][cmd][data][crc lo][crc hi]`; 200-byte payload → 4 frames, seq bits `0x80,0x81,0x82,0x03`, continuation `len=data+2`; `Frame.Parse` round-trip; notification detection (`reqId==0 && seq==0`).
- [ ] Write tests, see them fail, implement `Crc16`, `Frame.Build(sid, reqId, feat, cmd, data)` returning `byte[64][]`, `Frame.Parse(ReadOnlySpan<byte>)`. Pass. Commit.

### Task 3: QLinkClient over an `IHidTransport` (TDD with `FakeTransport`)
Interface: `IHidTransport { void Write(ReadOnlySpan<byte> report65); int Read(Span<byte> report65, int timeoutMs); void Dispose(); }`.
Tests: replies with wrong reqId are skipped; non-zero status → `QLinkException(status)`; notifications invoke `Notification` event; command outside allowlist throws `InvalidOperationException` without writing; `OpenSession` parses SID/state/timeout; multi-frame reply concatenation.
- [ ] Implement `QLinkClient.Send(feat, cmd, data, timeoutMs)`, `OpenSession(clientType=2)`, `KeepAlive`, `CloseSession`, `RequestActive`, `WaitReady`. `HidSharpTransport` finds VID 0x373F PID 0x0001 interface with 65-byte output report (MI_02), refuses PID 0x0009. Pass. Commit.

### Task 4: MediaDock API and DockConfig
`DockConfig` record: `MenuColor (r,g,b)`, `Clock24h`, `ScreensaverMode (Off/Clock/Image)`, `IdleSeconds`, `ScreenOffSeconds`; `ToBytes()/FromBytes()` (9 bytes). `MediaDock`: `GetState()`, `GetConfig()`, `SetConfig()`, `SetDateTime(DateTime local)` (u32 seconds of local wall clock since epoch), `SetImageChunk(slot, offset, data)`.
Tests: `DC4D0001021E003C00` ↔ record round-trip; date encoding.
- [ ] TDD, commit.

### Task 5: Rgb565 + FrameUploader (TDD with stalling fake)
`Rgb565.FromBitmap(SKBitmap)` → 153,600 bytes LE. `FrameUploader.Upload(byte[] rgb565, CancellationToken)`: header then 4,000-byte chunks; on `TimeoutException` → `WaitReady()` → restart from header; ≤3 restarts then return `false`; serialised by a lock.
Tests: exact write sequence (header + 39 chunks, offsets 9, 4009, …); a stall at chunk 5 → restart, second pass complete; 4 stalls → false; concurrent calls do not interleave.
- [ ] TDD, commit.

### Task 6: DockConfigGuard + DockConnection + IoCenterDetector
Guard: backup file `%APPDATA%\DarkmountHub\dock-config-backup.hex`, written once; rejects test configs (idle < 5 s or screen-off == 0) and substitutes `DC4D0001021E003C00`. Connection: state machine Disconnected → Paused(IoCenter) → Connecting → Ready, `Present(byte[] frame)` uploads; after first successful frame applies running config (image, idle = setting, off = 0); SetDateTime on connect and hourly; keep-alive timer 1 s when idle; `Suspend()` restores config and closes; HID errors → reconnect loop every 2 s. Detector: process named `IO_Center` running.
Tests: guard logic (unit); state transitions using fake transport.
- [ ] TDD, commit.

### Task 7: Sensors
`Snapshot` record: `CpuTemp, CpuPower, CpuLoad, GpuTemp, GpuPower, GpuLoad, RamUsedMb, RamTotalMb, VramUsedMb, VramTotalMb, Fps, FpsLow, FpsLowLabel ("0.1% low"/"1% low"), GameName` (nullable doubles). `MahmReader.Parse(byte[])`, `HwInfoReader.Parse(byte[], labels)`, `RtssReader.Parse(byte[], nowTicks)`; `SharedMemory.TryRead(name)`; `SystemInfo.RamTotalMb`, `VramTotalMb` (largest adapter); `SensorHub.Sample()` merges HWiNFO → MAHM per field; GPU auto-pick = `GPUn` with the most entries/highest power; game = foreground pid with RTSS fps > 0 updated < 2 s, not in exclude list.
Tests: synthetic MAHM/RTSS/HWiNFO blobs built in tests with the verified offsets; FLT_MAX → null; `GPU2` prefix handling; 0.1% low preferred over 1% low.
- [ ] TDD, then a live smoke run printing a snapshot. Commit.

### Task 8: Screens
Theme (colours per spec §6), `History` ring buffer (60), `WaveGraph.Draw(canvas, rect, series[], color, fillSeries)`, `StatsScreen` (no-game 3×80 px, game 60/60/40/80 px layout), `AlertOverlay` (36 px banner), `ScreenContext { Snapshot, Histories, Alerts, Now }`.
Tests: render each state to PNG in `tests/.../snapshots/` (null values, no game, game, alert); assert size 320×240 and non-uniform pixels.
- [ ] TDD, review PNGs visually, commit.

### Task 9: Animation sources
`IFrameSource.NextFrame(SKCanvas)`: `GifSource` (SKCodec frames), `FolderSource` (images, alphabetical), `VideoSource` (WinRT `MediaComposition.GetThumbnailAsync` every 2 s of video time), `ThemeSources` Plasma/Matrix/Starfield. `AnimationScreen` cover-scales frames.
Tests: GIF created in-test via Skia? (Skia can't encode GIF) → use a checked-in 3-frame GIF generated by a tiny encoder in the test; themes render non-empty.
- [ ] Implement, test, commit.

### Task 10: App logic (TDD): AlertEngine, AutoSwitcher, AppSettings/SettingsStore
Alert rules per spec §7 with hold 10 s and FPS-duration 4 s; AutoSwitcher per spec §7 (manual choice holds until next game start/stop). Settings JSON round-trip with defaults.
- [ ] TDD, commit.

### Task 11: FramePipeline + tray host
Pipeline loop (2 s cadence, immediate refresh on new alert, skip identical frame), `TrayApp` (menu: Auto/Stats/Animation, Pause dock, Settings…, Start with Windows, Exit; tooltip with status and missing-source hints), `HotkeyWindow` (RegisterHotKey Ctrl+Alt+Shift+D), `Autostart` (HKCU Run), single instance mutex, `Log`.
- [ ] Build, run against the real keyboard (IO Center closed), verify frames appear. Commit.

### Task 12: Settings window
Tabs: General (default screen, idle delay, hotkey, autostart), Animation (source type + path/theme), Alerts (thresholds + enable), Sensors (label overrides, GPU choice), live preview of the current dock frame.
- [ ] Implement, manual test, commit.

### Task 13: Packaging, README, push
`dotnet publish` self-contained single-file win-x64 → `publish/DarkmountHub.exe`; README with setup (Afterburner monitoring items, optional HWiNFO shared memory, RTSS), usage, safety notes. Push to origin.
- [ ] Commit, push.
