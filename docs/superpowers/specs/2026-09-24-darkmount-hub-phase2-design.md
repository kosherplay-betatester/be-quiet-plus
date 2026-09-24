# Darkmount Hub — Phase 2 design: full keyboard customisation

Status: 2026-09-24. The user asked for "the keyboard to be as customisable as the hardware allows, using software".
Protocol reference: `docs/QLINK_KEYBOARD.md` (keyboard, lighting, bindings) and `docs/QLINK_PROTOCOL.md`.

## 1. Scope

| Area | What the user gets | Mechanism |
|---|---|---|
| Lighting effects | On/off, 6 on-board effects (Static, Color wave, Tornado, Breathing, Reactive, Matrix), single/dual/gradient colours (up to 7 stops), direction, brightness, speed | LIGHTINGS 1/2/5/6, layer 0, mode General |
| Per-key RGB | Paint individual keys; host-driven effects (e.g. keyboard colour follows CPU temperature) | Standard HID **LampArray** (interface MI_03, usage page 0x59) — separate task, after the rest |
| Key remapping | Any key on the normal and Fn layers → another key/combination, F13–F24, media, mouse (incl. double-click/auto-fire), scroll, Windows shortcuts, backlight actions, special characters, disable; restore default | BINDINGS 1–5 |
| Game Mode locks | Choose which combinations Game Mode blocks (Win, Alt+Tab, Alt+F4, Shift+Tab, Caps Lock); toggle Game Mode | KEYBOARD 2/3/4/5 |
| Display keys | Image per key (with backup/restore of the originals) + an action per key | NUMPAD 2/3 + BINDINGS |
| Dock buttons | Actions for the 4 dock buttons (dock must be in its CUSTOM mode) | BINDINGS keys 117–120 |
| Dock settings | Menu colour, clock format, idle and screen-off timeouts used when the app is not driving the dock | MEDIA_DOCK 3 + config backup |
| Macros | Recorded or edited sequences (keys, text, delays, mouse, launch app, open URL/folder), triggered by any key | **Host-side**: key bound to F13–F24 (+ modifiers) on the keyboard; the app catches the hotkey and plays the macro with SendInput |
| Launch actions | "Open file/app/folder/URL" on any key | Same host-side trigger mechanism |
| Profiles | Save/apply complete setups (lighting, bindings, locks, display-key images, dock settings); auto-switch per game | Host-side JSON + apply sequence; game detection from phase 1 |

Out of scope: firmware macro storage (format unknown), keyboard layout switching (the web app follows it with a factory reset),
polling rate (never exercised on keyboards), snap-tap (unsupported on this model), calibration.

## 2. Safety rules

1. Allowlist gains only the reads and the normal persistent writes the web app itself uses: KEYBOARD 1/2/3/4/5,
   LIGHTINGS 1/2/5/6/14, BINDINGS 1/2/3/4/5. Never: DFU, FactoryReset, SetSerialNumber, raw layout report, SetCalibration,
   SetPollingRate, layer mask/layout/playback/realtime, macro Set commands, snap-tap.
2. **Before the first write of each kind**, the app reads and stores the keyboard's current state in
   `%APPDATA%\DarkmountHub\keyboard-backup\` (lock mask, lighting mode + layer config, all bindings, display-key JPEGs)
   and offers "Restore my original keyboard settings".
3. Writes happen only on explicit user actions (Apply/Save) or profile switches — never in a loop.
4. All writes go through the shared session (`DockConnection.TryExecute`) between dock frames.

## 3. Architecture

- `Darkmount.Keyboard` (library): `KeyboardSettings` (feature 7), `Lighting` + `LightingConfig` model (16),
  `Bindings` + `Binding` model with encoder/decoder (17), `KeyIds` table + `KeyGeometry` (ANSI layout) + `HidUsage`
  table, `DisplayKeys` (done), `KeyboardBackup` (read all → JSON + images; restore), `KeyboardState` (full snapshot used
  by profiles and backup).
- `Darkmount.App`: pages Lighting, Keys (keyboard picture editor, normal/Fn layer), Display keys, Dock buttons,
  Macros, Profiles; `MacroEngine` (SendInput player) + `TriggerHotkeys` (RegisterHotKey for F13–F24 combos);
  `ProfileManager` (save/apply, per-game auto switch).
- Per-key RGB (`LampArray`) is a follow-up task in the same library once the rest is verified on hardware.

## 4. Testing

Unit tests for every encoder/decoder against the byte examples in `QLINK_KEYBOARD.md` Appendix A; binding round trips for all
types; macro serialisation; profile apply order with a fake transport. Hardware checks with IO Center closed: read-only
dump first (lighting config, bindings, lock mask), then one reversible write per area (e.g. change lighting colour and
restore), with the user watching.
