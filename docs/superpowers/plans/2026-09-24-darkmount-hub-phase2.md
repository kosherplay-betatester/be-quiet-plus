# Darkmount Hub Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Everything the Dark Mount hardware allows, customisable from Darkmount Hub (spec: `docs/superpowers/specs/2026-09-24-darkmount-hub-phase2-design.md`).

**Architecture:** Protocol encoders/decoders live in `Darkmount.Keyboard` (pure, unit-tested against the byte examples in `docs/QLINK_KEYBOARD.md`). The app talks to the keyboard only through `KeyboardService` → `DockConnection.TryExecute` (one shared session, between dock frames). Per-key RGB uses the standard HID LampArray interface (MI_03) directly. Macros run on the PC, triggered by keys bound to F13–F24.

**Tech Stack:** .NET 10 WinForms, HidSharp, SkiaSharp, xUnit.

---

### Task 1: Keyboard protocol library (helper agent)
KeyIds, HidUsage, KeyGeometry, KeyboardSettings (locks, Game Mode), Lighting (+ effect metadata), Bindings (all action types, paging, defaults), KeyboardBackup (snapshot, save-once, minimal Apply). Allowlist additions only for the listed reads/normal writes.
- [ ] Tests against Appendix A golden bytes; allowlist negative tests. Commit.

### Task 2: Macro engine (helper agent)
Model + JSON, SendInput sink, shell launcher, player (Once/RepeatCount/Toggle, balanced key-ups), recorder (low-level hooks + post-processing), hotkey triggers F13–F24 (+modifiers), store, examples.
- [ ] Tests with fake sink/shell/keystate. Commit.

### Task 3: LampArray per-key RGB (helper agent)
Descriptor parser (real fixture), device open/attributes/lamp enumeration (read-only on hardware), multi/range update + autonomous-mode encoders, lamp→key mapping, LampProbe tool.
- [ ] Tests on the real descriptor fixture. Commit.

### Task 4: App shared infrastructure (done)
KeyboardService, Display keys page, Dock settings page, KeyboardView, ColorButton.

### Task 5: Keyboard backup on first write
On the first keyboard write of the session: `KeyboardBackup.Read(q, includeDisplayKeys: true)` → `SaveOnce` in `%APPDATA%\DarkmountHub\keyboard-backup`. A "Restore my original keyboard settings" button on the Profiles page applies it.
- [ ] Test with fake device: backup happens once, before the first write. Commit.

### Task 6: Lighting page
Mode on/off; effect list from metadata; colour mode tabs limited to what the effect allows; up to 7 gradient stops (ColorButtons + position sliders); direction buttons as allowed; brightness/speed 10–100 step 10; live preview strip; Apply → SetMode(General) + SetLayerConfig(0). Per-key tab (after Task 3 verified): KeyboardView multi-select + colour → LampArray; "reactive to CPU temperature" host effect.
- [ ] Render test of the page; hardware check with the user (change colour, then restore). Commit.

### Task 7: Keys page
KeyboardView of the whole keyboard (layer switch Normal / Fn); selecting a key shows its current binding; action editor for every action type (key + modifiers, F-keys incl. F13–F24, media, mouse, scroll, Windows shortcut, backlight, special character, disable, macro trigger, launch program/URL/folder → host trigger), "Restore default" (ClearBinding), master enable switch, Game Mode lock checkboxes + Game Mode toggle; locked keys disabled.
- [ ] Unit tests for action ↔ UI mapping; hardware check (remap a harmless key, restore). Commit.

### Task 8: Macros page
List + editor (steps grid, record button, test play), trigger assignment (picks a free F13–F24 combo and optionally binds a physical key to it via Bindings).
- [ ] Commit.

### Task 9: Profiles page
Save current (keyboard snapshot + dock screen settings + macro set name) as a named profile; apply; per-game auto-switch rules (game exe → profile, default profile when no game); restore original keyboard backup.
- [ ] Tests for apply order and auto-switch rules. Commit.

### Task 10: Integration, README, publish, push
- [ ] Full test suite, hardware acceptance with the user, README section for phase 2, publish single-file exe, push.
