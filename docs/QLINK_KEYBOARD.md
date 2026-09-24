# QLink keyboard customisation — be quiet! Dark Mount (P200)

Companion to `QLINK_PROTOCOL.md` (framing, CRC, sessions, media-dock/numpad image upload are documented there and
are **not** repeated). Everything here was read from the IO Center Web bundle
`scratchpad/web/pretty.js`; all `Lxxxxx` references are line numbers in that file.
No USB traffic was generated for this document.

Confidence: **HIGH** = read directly from code · **MED** = strong inference from code/UI text ·
**LOW** = guess / not implemented by the web app.

Device class `Kn` (L16570), `deviceType = "P200"`, `deviceCodeName = "Dark Mount"`,
declared features `[ROOT, DEVICE_INFO, KEYBOARD, LIGHTINGS, BINDINGS, NUMPAD_MODULE, MEDIA_DOCK_MODULE]` (L16600).
Key-id enum used by `Kn` = `x` (L9669). **Do not mix** with `L` (L9670, Light Mount P149) or `P` (L9672, Light Mount TKL K3).

---

## 0. Summary — what is and is not in the bundle

| Feature | Commands with a real encoder/decoder in the bundle (payload known) | Declared in enum only (payload **unknown**) |
|---|---|---|
| 7 KEYBOARD | 1–7 (all) | — |
| 16 LIGHTINGS | 1, 2, 3, 4, 5, 6, 9, 10, 14, 15, 16 | **7 GetLayerMask, 8 SetLayerMask, 11 GetPlaybackState, 12 SetPlaybackState, 13 PerformRealtimeUpdates** |
| 17 BINDINGS | 1–5 (6 only as a raw pass-through, unused) | — |
| 18 MACROS | 9 StartRecording, 10 StopRecording (used as a live key-press observer) | **1–8, 11** (whole macro store/playback) |
| 32 NUMPAD_MODULE | 1–3 | — |
| 33 MEDIA_DOCK_MODULE | 1–7 | 8–11 (desktop only) |
| 6 USB_DEVICE | 1–2 | — |

Wrapper methods `Lighting_GetLayerMask/GetPlaybackState/PerformRealtimeUpdates` exist (L16199, L16225, L16229) but the
dispatcher `_getLightingCommand` (L61274) has no `case` for 7/8/11/12/13 → it throws `Unexpected command`. Likewise
`_getMacrosCommand` (L61334) only handles 9/10. HIGH.

What the web UI actually does for the Dark Mount: one lighting layer (id 0, "TOP") in mode *General* with 6 on-board
effects; key remapping on two layers (Common / Fn); Game-Mode key-lock mask; layout switch; numpad display-key images;
media-dock config. **No per-key lighting, no macros, no Quick-Tap (snap-tap) for P200.** The welcome dialog
(L2684–L2691, en) lists as *desktop-only*: "Custom lighting with up to 7 layers", "Support for Windows Dynamic
Lighting", "Access to additional key rebinds, including open file, start application, open folder, Windows shortcuts,
profile switching, and on-the-fly lighting settings", "Macro editor".

### 0.1 Notification ids for these features (not in QLINK_PROTOCOL.md) — HIGH
| Feature | Notif enum | Ids |
|---|---|---|
| 7 KEYBOARD | `uw` L10181 | 1 ConfigChanged, 2 **StateChanged**, 3 SnapTapConfigChanged |
| 16 LIGHTINGS | — | no notification enum in the bundle |
| 17 BINDINGS | `Ew` L10199 | 1 BindingChanged, 2 **ActionFired**, 3 ConfigChanged |
| 18 MACROS | `Cw` L10201 | 1 MacrosChanged, 2 MacroConfigChanged, 3 MacroEventsChanged, 4 MacroNameChanged, 5 **InputEvent** |

Handlers registered by `Kn` (L16897): DEVICE_INFO/1, NUMPAD/1, MEDIA_DOCK/1, **KEYBOARD/2** (game mode), **MACROS/5** (input event).

### 0.2 Corrections / additions to QLINK_PROTOCOL.md
1. **§2.6 startup is not read-only.** `Kn.readConfig` (L16836) *writes* twice: `MediaDock_SetDateTime` (already noted) **and
   `Lighting_SetLightingMode(1)` whenever `GetLightingMode` returns > 1** (L16868) — i.e. the web app forcibly takes the
   keyboard out of desktop *Custom*/*Realtime* lighting. Exact order: OpenSession → GetActiveSessionInfo → GetQLinkVersion →
   GetSerialNumber → GetSupportedFeatures → GetDeviceInfo → (if active client ≠ QControl) Numpad_GetState → MediaDock_GetState →
   **Bindings_GetBindings(0)** → **Keyboard_GetConfig** → GetDeviceInfo (mandatory-update check, fw < 1.13.0) → **Keyboard_GetLayout** →
   **Keyboard_GetState** (only if keyboard MCU fw ≥ 1.2.0, L16851) → MediaDock_GetConfig → MediaDock_SetDateTime →
   **GetPollingRate** → **Lighting_GetLightingMode** → [SetLightingMode(1)] → **Lighting_GetLayerConfig(0)** →
   **Bindings_GetConfig** → **Bindings_GetBindings(0)** (again).
   If the active session belongs to another *Web* client, the web app silently adopts that SID (L16844).
2. **§6 `_setKeyboardLayout`**: the web app follows the raw `[FF FE 00 layout]` write with **DEVICE_INFO FactoryReset**
   (L53883–L53885 → `d2` L27926 → `FactoryReset` L16154). UI text: "This will reset the device to factory settings." (L3041).
3. **§3 MACROS** full list: 1 GetMacros, 2 SetMacros, 3 GetMacroConfig, 4 SetMacroConfig, 5 GetMacroEvents, 6 SetMacroEvents,
   7 GetMacroName, 8 SetMacroName, 9 StartRecording, 10 StopRecording, 11 AbortPlayback (`ns` L10200).
4. **§3.1 media dock**: there are no separate "enabled" flags on the wire: `screensaverEnabled ⇔ screensaverMode ≠ 0`
   (enabling writes mode 2 = Image, L29460) and `screenTurnOffEnabled ⇔ turnOffTimeout > 0` (disabling writes 0, L29569; parse L60712).
5. **§6 safe set** can be extended with the normal (persistent) configuration writes documented below: KEYBOARD 3,
   LIGHTINGS 2/6, BINDINGS 2/3/5. They are what the web app itself sends on every settings change.

---

## 1. KEYBOARD (feature 7)

### 1.1 Commands — HIGH (format)
| Cmd | Name | Request data | Response data | Class / line |
|---|---|---|---|---|
| 1 | GetLayout | — | `[physical u8][visual u8]` | `one` L58672 |
| 2 | GetConfig | — | `[lockMask u8]` | `sne` L58643 |
| 3 | SetConfig | `[lockMask u8]` | (web returns `data[1]`, ignore) | `rne` L58628 |
| 4 | GetState | — | `[state u8]` | `lne` L58696 |
| 5 | SetState | `[state u8]` | — | `cne` L58710 |
| 6 | GetSnapTapConfig | — | `[n] { [keyA u16 LE][keyB u16 LE][priority u8] } × n` | `dne` L58724 |
| 7 | SetSnapTapConfig | `[n] { [keyA u16 LE][keyB u16 LE][priority u8] } × n` | — | `une` L58763 |

```js
// rne.createOutgoingPacket (L58636) — SetConfig
[Dr.ALT_TAB*row.ALT_TAB + Dr.Win*row.Win + Dr.ALT_F4*row.ALT_F4 + Dr.SHIFT_TAB*row.SHIFT_TAB + Dr.CAPS_LOCK*row.CAPS_LOCK]
// one._build (L58681) — GetLayout
const t = wA[A[0]]; let i = n[A[1]];
t === "ANSI" && i !== "US" ? i = "US" : t === "ISO" && i === "US" && (i = "UK");
// dne._build (L58733) — GetSnapTapConfig
for (i < A[0]) { r = i*5+1; keyA = A[r+1]<<8 | A[r]; keyB = A[r+3]<<8 | A[r+2]; prio = A[r+4]; }
// une (L58771) — pairs whose keyA or keyB == 0 are dropped before sending
t = keys.filter(i => i.items[0].id !== 0 && i.items[1].id !== 0); [t.length, ...keyA LE, ...keyB LE, prio, ...]
```

### 1.2 Field meanings
**Layout** (`wA` L10208, `n` L10209) — HIGH
| physical | | visual | |
|---|---|---|---|
| 0 | ANSI | 0 | US |
| 1 | ISO | 1 | DE |
| 2 | JIS | 2 | UK |
| | | 3 | FR |
| | | 4 | NO |

Dark Mount layouts offered in the UI (`g$` L51905): `ANSI US`, `ISO UK`, `ISO DE`, `ISO FR`.

**lockMask — "Game Mode" key-combination lock** (`Dr` L10207, UI `h$` L51890) — HIGH
| bit | value | field | UI text (en) |
|---|---|---|---|
| 0 | 0x01 | SHIFT_TAB | "Disable SHIFT + TAB" |
| 1 | 0x02 | ALT_F4 | "Disable ALT + F4" |
| 2 | 0x04 | Win | "Disable Windows Key" |
| 3 | 0x08 | ALT_TAB | "Disable ALT + TAB" |
| 4 | 0x10 | CAPS_LOCK | "Disable CAPS LOCK" |

Semantics (tooltip L2531): "Press FN+PAUSE to turn on/off Game Mode. Game mode disables selected key combinations
from the list to avoid accidental minimizing in the heat of battle." → the mask only takes effect while Game Mode is on. MED.
Gotcha: `Kn.getDefaultConfig().keyConfig` (L16784) lacks `CAPS_LOCK`; `16*undefined = NaN` would make the whole byte 0 —
always send a fully-populated mask.

**state** (L16855, L23707–L23710, L61056–L61060) — HIGH (bits), MED (Dark Mount writes)
| bit | meaning |
|---|---|
| 0 | Game Mode on |
| 1 | Snap Tap / "Quick Tap" enabled |

Web write form: `SetState((snapTapEnabled << 1) + gameModeEnabled)` (L61059) — issued only when the K3 Quick-Tap switch
changes. For the Dark Mount the web app never writes state; Game Mode is toggled on the keyboard (Fn+Pause; Fn-layer Pause
is locked, L17043) and the UI shows it read-only from the **KEYBOARD/StateChanged** notification: `state = data[1] & 1`
(L16913; K3 also reads `data[1] >> 1 & 1`, L23765). `data[0]` of that notification is unknown. Writing bit0 via SetState
should toggle Game Mode on the Dark Mount (MED — untested by the web app).

**Snap-tap priority** (`Iw` L10245): 0 LastInput (default, L53671), 1 AbsolutePriorityKey1, 2 AbsolutePriorityKey2, 3 Neutral.
Max **5 pairs** (`Kp = 5`, L53661). UI name "Quick Tap". Only shown for K3 (`U = () => K3`, L53936); `Kn.SnapTapDisabledKey = []`,
`Kn.readConfig` never reads it → support on the Dark Mount firmware is **unknown** (LOW). Try GetSnapTapConfig (read-only) first.

**Not present in the bundle**: debounce, N-key-rollover, rapid trigger / actuation, permanent Win-lock, sleep timers for the
keyboard. A permanent Windows-key lock can be emulated by binding `KEY_ID_LWIN` (103) / `KEY_ID_APP` (104, "Right Win") to
*Disable* (§3).

---

## 2. LIGHTINGS (feature 16)

### 2.1 All 16 commands
| Cmd | Name | Request | Response | Conf | Class / line |
|---|---|---|---|---|---|
| 1 | GetLightingMode | — | `[mode]` (`buffer[7]`) | HIGH | `fne` L58781 |
| 2 | SetLightingMode | `[mode]` | — | HIGH | `pne` L58795 |
| 3 | GetLayersLayout | — | `[count][layerId…]` | HIGH fmt, unused | `hne` L58810 |
| 4 | SetLayersLayout | `[count][layerId…]` | — | HIGH fmt, unused | `gne` L58833 |
| 5 | GetLayerConfig | `[layerId]` | LayerConfig **without** layerId (§2.3) | HIGH | `mne` L58851 |
| 6 | SetLayerConfig | `[layerId]` + LayerConfig | — | HIGH | `Ene` L58918 |
| 7 | GetLayerMask | ? | ? | LOW | — |
| 8 | SetLayerMask | ? | ? | LOW | — |
| 9 | GetLayerName | `[layerId]` | `[len][ASCII…]` (web slices `data[1..len)`, dropping the last char — web bug) | HIGH fmt, unused | `Cne` L58958 |
| 10 | SetLayerName | `[layerId][len][ASCII…]` | — | HIGH fmt, unused | `vne` L58973 |
| 11 | GetPlaybackState | ? | ? | LOW | — |
| 12 | SetPlaybackState | ? | ? | LOW | — |
| 13 | PerformRealtimeUpdates | ? | ? | LOW (see §2.6) | — |
| 14 | GetGlobalLayers | — | `[count][layerId…]` | HIGH fmt | `Pie` L60807 |
| 15 | GetCalibration | — | 14 bytes (§2.5) | HIGH | `Ine` L58992 |
| 16 | SetCalibration | 14 bytes | — | HIGH, **dangerous** | `yne` L59037 |

`GetGlobalLayers` is used only by the K3 (L23689): returned ids map to `jl` (L23384) 0 = TOP (keys), 1 = BARS (light bars);
if count = 0 the K3 falls back to all ids. For the Dark Mount the web app hard-codes `LightingZones = [TOP]` → layer **0**
(L16212). Expect `[01][00]` (MED).

### 2.2 Lighting mode (`zi` L10224) — HIGH values, MED meaning
| value | name | meaning |
|---|---|---|
| 0 | Off | LEDs off (web "illumination on/off" switch → `SetLightingMode(enabled ? 1 : 0)`, L61023) |
| 1 | General | on-board effect engine driven by LayerConfig (the only mode the web app uses) |
| 2 | Custom | desktop "Custom lighting with up to 7 layers" (layers layout/mask/name commands) |
| 3 | Realtime | host-streamed colours (PerformRealtimeUpdates) |

### 2.3 LayerConfig payload — HIGH
```
SetLayerConfig data (Ene L58942):
 [0] layerId              Dark Mount: 0
 [1] effect   (ye)
 [2] direction (ke)
 [3] brightness           0..100 (UI slider 10..100 step 10)
 [4] speed                0..100 (UI slider 10..100 step 10)
 [5] colorMode (oe)       = colorData.index
 [6..] colour block:
    oe 0 SingleColor       R G B
    oe 1 DualColor         R1 G1 B1  R2 G2 B2
    oe 2 Gradient          N, then N × (R G B pos)        pos = 0..100 (% , Math.round)
    oe 3 OrientedGradient  gradType(ot), angle u16 LE (0..359°), N, then N × (R G B pos)
 [tail] only if effect == Ripple(7):  width (def 5)  range (def 7)  fadeOut (def 0)
GetLayerConfig(layerId) response data = the same bytes starting at [1] (no layerId echo):
 data[0]=effect (values > 100 are treated as Off), [1]=direction, [2]=brightness, [3]=speed, [4]=colorMode, [5..]=colours,
 Ripple extras = last 3 bytes of data (mne L58851–L58905)
```
```js
// Ene.createOutgoingPacket (L58926)
case oe.Gradient: A.push(data.length), data.forEach(i => A.push(i.first.r, i.first.g, i.first.b, Math.round(i.second)));
case oe.OrientedGradient: A.push(gradient_type ?? 0), A.push(...un(gradient_angle ?? 0)), A.push(data.length), ...
ye[row.effect] === ye.Ripple && t.push(row.width ?? 5, row.range ?? 7, row.fade_out ?? 0);
createTransmitPacket(SID, 16, 6, [zoneId, ye[row.effect], row.direction, row.brightness, row.speed, row.colorData.index, ...A, ...t])
```
Max gradient stops in the UI: **7** (`sl` `pointsLimit = 7`, L71855), min 2 (end points not removable). A 7-stop
gradient = 6 + 1 + 28 = 35 bytes → fits in one 55-byte frame; OrientedGradient 7 stops = 38 bytes. (12 stops would still fit.)

### 2.4 Enums and per-effect UI — HIGH
`ye` effects (L10228): **0 Static, 1 ColorWave, 2 Tornado, 3 Breathing, 4 Reactive, 5 Matrix, 6 Off, 7 Ripple** (0 is also
aliased "Unassigned"). `Bi` (L10220, used by Backlight bindings) has the same numbering.

Dark Mount effect list = base `ss.EffectList` (L16216, not overridden by `Kn`): Static, ColorWave, Tornado, Breathing,
Reactive, Matrix. English UI names (L2575–L2582, L3193): "static", "color wave", "tornado", "breathing", "reactive",
"matrix" (K3 additionally "Off", "Ripple"). Colour tabs: "single color", "dual color", "gradient"; sliders "direction",
"brightness", "speed".

`ke` direction (L10230): 0 Up, 1 Down, 2 Left, 3 Right, 4 Clockwise, 5 Counterclockwise, 6 Omnidirectional, 7 Horizontal,
8 Vertical, 9 Cross. `oe` colour mode (L10222): 0 SingleColor, 1 DualColor, 2 Gradient, 3 OrientedGradient.
`ot` gradient type (L10241): 0 Linear, 1 Radial, 2 Conic (UI offers Linear/Conic, K3/W1/F1 only).

Effect panels are selected in `hce` (L73811): Static→`$B` L72042, ColorWave→`ix` L72419, Tornado→`ax` L72571,
Breathing→`rx` L72717, Reactive→`Ole` L72846, Matrix→`Hle` L72993. Defaults from `tV(P200)` (L14229) and `Pn("P200")` = FF2800 (L12721):

| Effect (id) | colour modes offered | direction buttons | speed slider | default |
|---|---|---|---|---|
| Static (0) | Single only on P200 (tabs only for K3/W1/F1) | — (sends stored value, def 0) | no | single FF2800, bright 100 |
| ColorWave (1) | Single, Dual, Gradient | Left 2, Up 0, Down 1, Right 3 (`dle` L72397) | 10–100 | Single FF2800, dir 3 (Right) |
| Tornado (2) | Single, Dual, Gradient | Clockwise 4, Counter-clockwise 5 (`Ele` L72557) | 10–100 | Gradient rainbow, dir 4 |
| Breathing (3) | Single, Dual, Gradient | — (def 0) | 10–100 | Gradient rainbow, dir 0 |
| Reactive (4) | Single, Dual, Gradient | — (def 0) | 10–100 | Dual FF2800 → FFFFFF, dir 0 |
| Matrix (5) | Dual, Gradient | Left, Up, Down, Right (`Tle` L72971) | 10–100 | Gradient 0D0208 / 003B00 / 008F11…, Dual 00FF00 on 000000, dir 1 |

Brightness: `BRIGHTNESS_MIN = 10` (L73213), max 100, step 10, same for speed (e.g. L72414–L72418).
Rainbow default `yr` (L12629): FF0000@0, FFFF00@16.67, 00FF00@33.33, 00FFFF@50, 0000FF@66.67, FF00FF@83.33, FF0000@100
(wire positions 0,17,33,50,67,83,100). `Kn` factory default (L16601): Static, Single FF2800, dir Right, speed 50, **brightness 50**.

Brightness scale: `Kn.readConfig` uses the device value unchanged (0..100). The Light Mount (P149) class rescales on read
(`Math.ceil(brightness / 255 * 100)`, L36252) but writes 0..100 — so verify on your unit by reading back after a write. MED.
Speed: larger = faster (preview timing e.g. `1500 - speed/10*100` ms, L35137). MED.

Persistence: there is no "save" command; the web app re-reads mode/LayerConfig from the device on every connect and keeps
no persistent host copy (§7) → settings are stored on the keyboard. MED. The web app retries a failed SetLayerConfig once
after 250 ms (L61037).

### 2.5 Calibration (15/16) — HIGH format, **dangerous**
```
14 bytes: [version u8][R gain f32 LE][G gain f32 LE][B gain f32 LE][flags u8: bit0 = enabled]
gain = UI percent / 100 ; response shorter than 14 bytes → web assumes {enabled:false, 100/59/43}
```
K3 auto-writes `ld = {version 1, enabled, R 100, G 51, B 37 %}` when version is 0 (L23699–L23704). Never used for the Dark
Mount. Factory white-balance → do not write.

### 2.6 Per-key / realtime lighting — NOT recoverable from this bundle
* `PerformRealtimeUpdates` (13), `Get/SetLayerMask` (7/8), `Get/SetPlaybackState` (11/12): no encoder, no caller. LOW.
* Closest analogue that *is* implemented: **ARGB_DEVICE (50) cmd 8 PerformColorUpdates** (`fie` L60170, helpers L15647):
  `[channel][nUpdates] { [subCmd][args] } × n`, `Nu` (L10226): 0 **Commit** `[]`, 1 **SetColors** `[start u16 LE][R G B]…`,
  2 **SetRangeColor** `[start u16 LE][end u16 LE][R G B]`, 3 **SetColor** `[index u16 LE][R G B]`. The web app always ends a
  batch with Commit and only streams when the channel mode is Realtime (L49050). A LIGHTINGS-13 payload of the same shape
  (`[layer?][n]{…}` with key ids or LED indices) is plausible but **unverified — do not send blindly**.
* Windows Dynamic Lighting is advertised (desktop-only) — check read-only (HID enumeration, no writes) whether the keyboard
  exposes a **HID LampArray** top-level collection (usage page 0x59). If it does, per-key RGB is available through the
  standard `Windows.Devices.Lights.LampArray` API without any vendor protocol. Otherwise capture the desktop app
  (USBPcap) while it edits Custom layers / Dynamic Lighting.
* Layer mask per key would need the key-id table (§3.6) and LED geometry (§8).

---

## 3. BINDINGS (feature 17)

### 3.1 Commands
| Cmd | Name | Request data | Response data | Conf | Class / line |
|---|---|---|---|---|---|
| 1 | GetBindings | `[startIndex u16 LE]` | `[a][b]` + records; `Total = a + b` | HIGH fmt / MED meaning of a,b | `Une` L59143 |
| 2 | SetBinding | one record (§3.3) | (web returns `data[1]`) | HIGH | `kne` L59320 |
| 3 | ClearBinding | `[keyId][layerByte]` | — | HIGH | `bne` L59092 |
| 4 | GetConfig | — | `[enabled]` (`b2` 0 Disabled / 1 Enabled) | HIGH | `Bne` L59305 |
| 5 | SetConfig | `[enabled]` | — | HIGH | `Mne` L59394 |
| 6 | GetBinding | web passes its raw argument array (probably `[keyId][layerByte]`) | web reads `data[1]` | LOW, unused | `wne` L59108 |

`SetConfig/GetConfig` is the master "key binding on/off" switch (UI stub "key binding is turned off", L2598). HIGH.

**GetBindings paging**: request is a start index; the web app sums the first two bytes (`t = A[1] + A[0]`, L59155) — either a
u16 LE total or two per-layer counts (identical result below 256 entries). Records start at `data[2]`; parsing stops at
the end of `data`. K3/P149 page with `GetBindings(Count)` while `Count < Total` (L23712, L36236); `Kn` calls
`GetBindings(0)` (L16847, L16871) but never pages. Implement paging anyway. Long responses arrive as multi-frame messages (transport
concatenates). MED.

Only *custom* entries are returned. Firmware built-in Fn functions are not reported — `Kn.readConfig` merges its default Fn
list for display (L16872). MED.

### 3.2 Key addressing — HIGH
```
keyRef (u16 LE) = (layerByte << 8) | keyId       wire: [keyId][layerByte]
layerByte: 0x00 = Common ("remap" tab)   0x80 = Fn layer ("fn layer" tab)
```
```js
// protocol.setConfig (L61043)
K = N === 0 ? 0 : 128   // N = LA.Common(0) / LA.Fn1(1)
T.type === ie.Default ? runCommand(BINDINGS, ClearBinding, T.key, K) : runCommand(BINDINGS, SetBinding, T, K)
// kne (L59330)  t = [...un(this.layer << 8 | key), type]
// Une parse (L59165)  layer = i[a+1] >> 7 & 1 ; key = i[a+0] ; type = i[a+2]
```
`LA` (L10211): 0 Common, 1 Fn1. Key ids: §3.6.

### 3.3 Binding record — HIGH unless marked
```
[0] keyId  [1] layerByte (0x00 / 0x80)  [2] type (ie, L10212)  [3..] payload
```
Record lengths from `Ni` (L59123): header `Zi = 3` bytes.

| type | `ie` name | payload | record len | web UI | notes |
|---|---|---|---|---|---|
| 0 | Default | *(none)* | 3 | "Disable" | **wire type 0 = key disabled.** Web writes `ie.Disable` as type 0 (L59383) and maps type 0 → Disable on read (L59168). "Default" (restore the key) = **ClearBinding**. |
| 1 | StandardKey | `[modifiers (HA bitmask)][HID usage]` | 5 | yes | modifiers + key |
| 1 | StandardKey (F-key) | `[0x00][usage F1–F24]` | 5 | yes | `fKey` form (L59336) |
| 2 | Media | `[action (hA)]` | 4 | yes | |
| 2 | Media / SpecificSound | `[0x0A][len][ASCII audio-device id]` | 5+len | read only | desktop only (web writes only `[0x0A]`) |
| 3 | Mouse button | `[0x01][flags][autoFire]` | 6 | yes | flags = `(button−1) & 0x1F` \| `0x40` WhilePressed \| `0x80` DoubleClick; autoFire = clicks/s 1–50 when AutoFire else 0 |
| 3 | Mouse scroll | `[0x02][dir−1]` | 5 | yes | dir from `Ka` |
| 4 | OpenFolder | `[len][ASCII path]` | 4+len | desktop only | |
| 5 | OpenFile | `[len][ASCII path]` | 4+len | desktop only | "Open File/Start Application" |
| 6 | OpenBrowser | `[len][ASCII URL]` | 4+len | desktop only | factory default of B1 |
| 7 | WindowsShortcut | `[action (rt)]` | 4 | yes | |
| 8 | Profile | `[action (L3)]`; read side for SelectSpecific: `[0x01][16-byte ASCII profile UUID]` | 4 / 20 | desktop only | web *write* sends `[0x01][id u16 LE]` — inconsistent, LOW |
| 9 | Backlight | `[action (kt)]` (+ `[effect (Bi)]` if SelectEffect) | 4 / 5 | yes | "on-the-fly lighting" |
| 10 | Macro | read: `[action][macroId u8]` (len 5); web write: `[action][macroId u16 LE]` (len 6) | 5 / 6 | desktop only | LOW — meaning of `action` unknown (likely playback mode) |
| 11 | AltCode ("Special" tab) | `[0x02][codepoint u16 LE]` | 6 | yes | char list §3.7 |
| 12 | Disable | — | — | — | UI-only value, never on the wire (→ type 0) |

```js
// kne.createOutgoingPacket (L59326-L59392), abridged
case StandardKey: if (specialKey) t[last] = 11, t.push(2, sk & 255, sk >> 8 & 255);
                  else if (fKey) t[last] = 1, t.push(0, fKey);
                  else t.push(sum(modifiers), key);
case Media:  t.push(action);
case Mouse:  button ? t.push(1, (action-1 & 31) | (WhilePressed?64:0) | (DoubleClick?128:0), AutoFire ? autoFire : 0)
                    : t.push(2, action - 1);
case OpenFile/OpenFolder/OpenBrowser: t.push(url.length, ...ascii(url));
case WindowsShortcut: t.push(action);
case Profile: action === SelectSpecific ? t.push(action, ...un(parseInt(specificId))) : t.push(action);
case Backlight: action === SelectEffect ? t.push(action, effect) : t.push(action);
case Macro: t.push(action, ...un(parseInt(macroId)));
case Disable: t[t.length - 1] = 0;
case Default: -> ClearBinding [t[0], t[1]]
```
Web read caveat: a StandardKey is only decoded if its usage is in the web's 65-entry layout list or the F-key list; other
usages come back with empty data. Your parser should accept any HID usage 0x04–0xE7. Strings are sent as
`charCodeAt` bytes (ASCII/Latin-1; no UTF-8). Records longer than 55 bytes (long paths/URLs) go multi-frame.

**Enums** — HIGH
* `HA` modifiers (L10210, standard HID modifier byte): 0x01 LeftControl, 0x02 LeftShift, 0x04 LeftAlt, 0x08 LeftGui,
  0x10 RightControl, 0x20 RightShift, 0x40 RightAlt, 0x80 RightGui.
* `hA` media (L10213) — UI label en: 1 IncreaseVolume "Volume +", 2 DecreaseVolume "Volume -", 3 Mute "Mute",
  4 MicMute "Mic mute", 5 NextTrack "Next track", 6 PrevTrack "Prev track", 7 PlayPause "Play/Pause", 8 Stop "Stop",
  9 LaunchVolume "Volume mixer", 10 SpecificSound "Specific sound" (desktop only). 0 None.
* `Vn` mouse buttons (L10214; wire = value−1): 1 LeftBtn, 2 RightBtn, 3 MiddleBtn, 4 Forward, 5 Backward.
* `gi` mouse modes (L10215): "Single Click", "Double Click" (0x80), "While Pressed" (0x40), "Auto Fire" (3rd byte 1–50).
* `Ka` scroll (L10216; wire = value−1): 1 ScrollUp, 2 ScrollDown, 3 ScrollLeft, 4 ScrollRight.
* `rt` Windows shortcuts (L10217) — en label: 1 FileExplorer "File explorer", 2 Calculator, 3 TaskManager "Task manager",
  4 LockPC "Lock pC", 5 ShutDownPC "Shut down pC", 6 SleepPC "Sleep PC", 7 HibernatePC "Hibernate PC", 8 SnippingTool
  "Snipping tool", 9 Notepad, 10 Paint, 11 AirplaneMode "Airplane mode", 12 XboxGameBar "Xbox game bar", 13 SystemSettings
  "System settings", 14 Refresh, 15 TabbingApp "Tabbing application", 16 CloseApp "Close application", 17 Copy, 18 Paste,
  19 Cut, 20 InternetBrowser "Browser", 21 EmailReader "Mail". 0 None. (All 21 offered in `LAe` L55944.)
* `L3` profile actions (L10218): 0 None, 1 SelectSpecific, 2 SelectCycle, 3 SelectNext, 4 SelectPrevious.
* `kt` backlight actions (L10219): 0 None, 1 SelectEffect "select effect", 2 NextEffect "Next effect", 3 PrevEffect
  "Prev effect", 4 IncreaseBrightness "Increase Brightness", 5 DecreaseBrightness "Decrease Brightness".
  SelectEffect effect list for the Dark Mount (`OAe` L56102, non-K3): Static 0, ColorWave 1, Tornado 2, Breathing 3, Reactive 4, Matrix 5.
* `ie` binding types as shown in the UI "function" dropdown (`hte` L56984): Default, Standard Keys, Media, Mouse,
  Open File/Start Application, Open Folder, Open Browser, Windows Shortcut, Profile, Backlight, Macros, Disable. For the
  Dark Mount all are listed (`DisabledFunctionSelect = []`), but OpenFile/OpenFolder/OpenBrowser/Macro/Profile open a
  stub "This option cannot be managed through the web app. Please download the Windows desktop version" (`Ute` L57272).

**Host-executed actions — MED/LOW.** OpenFile/OpenFolder/OpenBrowser/Profile/SpecificSound/Macro cannot be executed by
keyboard firmware alone in an obvious way. BINDINGS notification **2 ActionFired** exists; the web app only subscribes to it
for the Light Mount (to animate key presses, `key = data[1] + data[0]`, L36277). Most likely the desktop app listens for
ActionFired and performs the host-side action. A C# app can do the same: store the binding on the device, then act on
ActionFired. Verify with IO Center closed whether B1's factory OpenBrowser binding does anything. LOW.

### 3.4 Factory defaults and locked keys (`Kn.getDefaultConfig` L16601, `Kn.DisabledKey` L17043) — HIGH
| Layer | Key | Binding |
|---|---|---|
| Common | 118 PLAY_PAUSE / 117 MUTE / 119 PREV / 120 NEXT (dock) | Media PlayPause(7) / Mute(3) / PrevTrack(6) / NextTrack(5) |
| Common | 109 B1 | OpenBrowser "https://www.bequiet.com/en" (len 26) |
| Common | 110 B2 … 116 B8 | WindowsShortcut 20 Browser, 1 File explorer, 21 Mail, 8 Snipping tool, 3 Task manager, 4 Lock PC, 6 Sleep PC |
| Fn | dock 117–120 | same media actions |
| Fn | 65 ↑ / 66 ↓ | Backlight IncreaseBrightness(4) / DecreaseBrightness(5) |
| Fn | 62 ← / 69 → | Backlight PrevEffect(3) / NextEffect(2) |

Not rebindable: Common — `KEY_ID_FN` (55). Fn layer — `KEY_ID_r` (19, "Hold FN + R for 5 seconds to reset the device to
factory."), `KEY_ID_FN`, `KEY_ID_PSE` (102, "Game Mode Turn ON/OFF").

Reset-to-default (`W6` L28445): every existing entry of the layer → ClearBinding, then SetBinding for each default; for the
Common layer the web app additionally re-uploads the 8 factory numpad images ("may take up to 15 seconds", L2652).

### 3.5 Media-dock buttons and the "CUSTOM" mode — MED
Dock buttons are ordinary binding keys: 117 Mute = *Top Left*, 118 Play/Pause = *Top Right*, 119 Previous = *Bottom Left*,
120 Next = *Bottom Right* (`tB` L42018, L2823–L2826). UI hint (L2834): "You should have to activate the "CUSTOM" mode on
your Media Dock Controller to have these rebinds be active." → CUSTOM is chosen **on the dock itself**; there is no QLink
command for it in the bundle. The dock dial has **no key id** for the Dark Mount (desktop key map L26251: `Key_DialRotate*`
/ `Key_Dial` map only to P149/K3) → not rebindable; behaviour (volume/menu) is firmware-fixed. LOW.

### 3.6 Key-ID table (`x`, L9669) — HIGH (ids/labels), MED (derived HID column)
Labels from `Kn.KeyMetaInfo` (ANSI L17074, ISO L18022). The last column is the *standard* HID usage the key would normally
send — derived from the key name, **not** present in the bundle. Ids 106–108 do not exist on the Dark Mount.
`KEY_ID_APP` (104) is labelled "Right Win" and sits between Alt Gr and Fn. `KEY_ID_ISO` (105) only exists physically on ISO
boards; `KEY_ID_BSL` (28) is ANSI "\" / ISO "#".

| id | hex | constant | zone | ANSI US label | ISO UK | ISO DE | ISO FR | default HID usage (derived) |
|---:|---:|---|---|---|---|---|---|---|
| 0 | 0x00 | KEY_ID_NULL | — |   |  |  |  |  |
| 1 | 0x01 | KEY_ID_TIL | keyboard | &#96; | &#96; | ^ | ² | 0x35 |
| 2 | 0x02 | KEY_ID_1 | keyboard | 1 | 1 | 1 | 1 | 0x1E |
| 3 | 0x03 | KEY_ID_2 | keyboard | 2 | 2 | 2 | 2 | 0x1F |
| 4 | 0x04 | KEY_ID_3 | keyboard | 3 | 3 | 3 | 3 | 0x20 |
| 5 | 0x05 | KEY_ID_4 | keyboard | 4 | 4 | 4 | 4 | 0x21 |
| 6 | 0x06 | KEY_ID_5 | keyboard | 5 | 5 | 5 | 5 | 0x22 |
| 7 | 0x07 | KEY_ID_6 | keyboard | 6 | 6 | 6 | 6 | 0x23 |
| 8 | 0x08 | KEY_ID_7 | keyboard | 7 | 7 | 7 | 7 | 0x24 |
| 9 | 0x09 | KEY_ID_8 | keyboard | 8 | 8 | 8 | 8 | 0x25 |
| 10 | 0x0A | KEY_ID_9 | keyboard | 9 | 9 | 9 | 9 | 0x26 |
| 11 | 0x0B | KEY_ID_0 | keyboard | 0 | 0 | 0 | 0 | 0x27 |
| 12 | 0x0C | KEY_ID_MIS | keyboard | - | - | ẞ | ° | 0x2D |
| 13 | 0x0D | KEY_ID_EQU | keyboard | = | = | &#96; | + | 0x2E |
| 14 | 0x0E | KEY_ID_BSP | keyboard | Backspace | Backspace | Backspace | Backspace | 0x2A |
| 15 | 0x0F | KEY_ID_TAB | keyboard | Tab | Tab | Tab | Tab | 0x2B |
| 16 | 0x10 | KEY_ID_q | keyboard | Q | Q | Q | A | 0x14 |
| 17 | 0x11 | KEY_ID_w | keyboard | W | W | W | Z | 0x1A |
| 18 | 0x12 | KEY_ID_e | keyboard | E | E | E | E | 0x08 |
| 19 | 0x13 | KEY_ID_r | keyboard | R | R | R | R | 0x15 |
| 20 | 0x14 | KEY_ID_t | keyboard | T | T | T | T | 0x17 |
| 21 | 0x15 | KEY_ID_y | keyboard | Y | Y | Z | Y | 0x1C |
| 22 | 0x16 | KEY_ID_u | keyboard | U | U | U | U | 0x18 |
| 23 | 0x17 | KEY_ID_i | keyboard | I | I | I | I | 0x0C |
| 24 | 0x18 | KEY_ID_o | keyboard | O | O | O | O | 0x12 |
| 25 | 0x19 | KEY_ID_p | keyboard | P | P | P | P | 0x13 |
| 26 | 0x1A | KEY_ID_OQO | keyboard | [ | [ | ü | ¨ | 0x2F |
| 27 | 0x1B | KEY_ID_EQO | keyboard | ] | ] | + | £ | 0x30 |
| 28 | 0x1C | KEY_ID_BSL | keyboard | \ | # | # | μ | 0x31 (ISO: 0x32) |
| 29 | 0x1D | KEY_ID_CAP | keyboard | Caps Lock | Caps Lock | Caps Lock | Caps Lock | 0x39 |
| 30 | 0x1E | KEY_ID_a | keyboard | A | A | A | Q | 0x04 |
| 31 | 0x1F | KEY_ID_s | keyboard | S | S | S | S | 0x16 |
| 32 | 0x20 | KEY_ID_d | keyboard | D | D | D | D | 0x07 |
| 33 | 0x21 | KEY_ID_f | keyboard | F | F | F | F | 0x09 |
| 34 | 0x22 | KEY_ID_g | keyboard | G | G | G | G | 0x0A |
| 35 | 0x23 | KEY_ID_h | keyboard | H | H | H | H | 0x0B |
| 36 | 0x24 | KEY_ID_j | keyboard | J | J | J | J | 0x0D |
| 37 | 0x25 | KEY_ID_k | keyboard | K | K | K | K | 0x0E |
| 38 | 0x26 | KEY_ID_l | keyboard | L | L | L | L | 0x0F |
| 39 | 0x27 | KEY_ID_COL | keyboard | ; | ; | ö | M | 0x33 |
| 40 | 0x28 | KEY_ID_CC | keyboard | ' | ' | ä | % | 0x34 |
| 41 | 0x29 | KEY_ID_RTN | keyboard | Enter | Enter | Enter | Enter | 0x28 |
| 42 | 0x2A | KEY_ID_LSHFT | keyboard | Left Shift | Left Shift | Left Shift | Left Shift | 0xE1 |
| 43 | 0x2B | KEY_ID_z | keyboard | Z | Z | Y | W | 0x1D |
| 44 | 0x2C | KEY_ID_x | keyboard | X | X | X | X | 0x1B |
| 45 | 0x2D | KEY_ID_c | keyboard | C | C | C | C | 0x06 |
| 46 | 0x2E | KEY_ID_v | keyboard | V | V | V | V | 0x19 |
| 47 | 0x2F | KEY_ID_b | keyboard | B | B | B | B | 0x05 |
| 48 | 0x30 | KEY_ID_n | keyboard | N | N | N | N | 0x11 |
| 49 | 0x31 | KEY_ID_m | keyboard | M | M | M | ? | 0x10 |
| 50 | 0x32 | KEY_ID_CMA | keyboard | , | , | , | . | 0x36 |
| 51 | 0x33 | KEY_ID_DOT | keyboard | . | . | . | / | 0x37 |
| 52 | 0x34 | KEY_ID_SL | keyboard | / | / | - | § | 0x38 |
| 53 | 0x35 | KEY_ID_RSHFT | keyboard | Right Shift | Right Shift | Right Shift | Right Shift | 0xE5 |
| 54 | 0x36 | KEY_ID_LCTRL | keyboard | Left Ctrl | Left Ctrl | Left Ctrl | Left Ctrl | 0xE0 |
| 55 | 0x37 | KEY_ID_FN | keyboard | Fn | Fn | Fn | Fn | — (Fn) |
| 56 | 0x38 | KEY_ID_LALT | keyboard | Left Alt | Left Alt | Left Alt | Left Alt | 0xE2 |
| 57 | 0x39 | KEY_ID_SPC | keyboard | Space | Space | Space | Space | 0x2C |
| 58 | 0x3A | KEY_ID_RALT | keyboard | Alt Gr | Alt Gr | Alt Gr | Alt Gr | 0xE6 |
| 59 | 0x3B | KEY_ID_RCTRL | keyboard | Right Ctrl | Right Ctrl | Right Ctrl | Right Ctrl | 0xE4 |
| 60 | 0x3C | KEY_ID_INS | keyboard | Insert | Insert | Insert | Insert | 0x49 |
| 61 | 0x3D | KEY_ID_DEL | keyboard | Delete | Delete | Delete | Delete | 0x4C |
| 62 | 0x3E | KEY_ID_LA | keyboard | ← | ← | ← | ← | 0x50 |
| 63 | 0x3F | KEY_ID_HOME | keyboard | Home | Home | Home | Home | 0x4A |
| 64 | 0x40 | KEY_ID_END | keyboard | End | End | End | End | 0x4D |
| 65 | 0x41 | KEY_ID_UA | keyboard | ↑ | ↑ | ↑ | ↑ | 0x52 |
| 66 | 0x42 | KEY_ID_DA | keyboard | ↓ | ↓ | ↓ | ↓ | 0x51 |
| 67 | 0x43 | KEY_ID_PUP | keyboard | Page Up | Page Up | Page Up | Page Up | 0x4B |
| 68 | 0x44 | KEY_ID_PDN | keyboard | Page Down | Page Down | Page Down | Page Down | 0x4E |
| 69 | 0x45 | KEY_ID_RA | keyboard | → | → | → | → | 0x4F |
| 70 | 0x46 | PAD_ID_NUM | numpad module | Num Lock | Num Lock | Num Lock | Num Lock | 0x53 |
| 71 | 0x47 | PAD_ID_7 | numpad module | Numpad 7 | Numpad 7 | Numpad 7 | Numpad 7 | 0x5F |
| 72 | 0x48 | PAD_ID_4 | numpad module | Numpad 4 | Numpad 4 | Numpad 4 | Numpad 4 | 0x5C |
| 73 | 0x49 | PAD_ID_1 | numpad module | Numpad 1 | Numpad 1 | Numpad 1 | Numpad 1 | 0x59 |
| 74 | 0x4A | PAD_ID_SL | numpad module | / | / | / | / | 0x54 |
| 75 | 0x4B | PAD_ID_8 | numpad module | Numpad 8 | Numpad 8 | Numpad 8 | Numpad 8 | 0x60 |
| 76 | 0x4C | PAD_ID_5 | numpad module | Numpad 5 | Numpad 5 | Numpad 5 | Numpad 5 | 0x5D |
| 77 | 0x4D | PAD_ID_2 | numpad module | Numpad 2 | Numpad 2 | Numpad 2 | Numpad 2 | 0x5A |
| 78 | 0x4E | PAD_ID_0 | numpad module | Numpad 0 | Numpad 0 | Numpad 0 | Numpad 0 | 0x62 |
| 79 | 0x4F | PAD_ID_MUL | numpad module | * | * | * | * | 0x55 |
| 80 | 0x50 | PAD_ID_9 | numpad module | Numpad 9 | Numpad 9 | Numpad 9 | Numpad 9 | 0x61 |
| 81 | 0x51 | PAD_ID_6 | numpad module | Numpad 6 | Numpad 6 | Numpad 6 | Numpad 6 | 0x5E |
| 82 | 0x52 | PAD_ID_3 | numpad module | Numpad 3 | Numpad 3 | Numpad 3 | Numpad 3 | 0x5B |
| 83 | 0x53 | PAD_ID_DEL | numpad module | . | . | . | . | 0x63 |
| 84 | 0x54 | PAD_ID_SUB | numpad module | - | - | - | - | 0x56 |
| 85 | 0x55 | PAD_ID_PLUS | numpad module | + | + | + | + | 0x57 |
| 86 | 0x56 | PAD_ID_ENT | numpad module | Enter | Enter | Enter | Enter | 0x58 |
| 87 | 0x57 | KEY_ID_ESC | keyboard | Escape | Escape | Escape | Escape | 0x29 |
| 88 | 0x58 | KEY_ID_F1 | keyboard | F1 | F1 | F1 | F1 | 0x3A |
| 89 | 0x59 | KEY_ID_F2 | keyboard | F2 | F2 | F2 | F2 | 0x3B |
| 90 | 0x5A | KEY_ID_F3 | keyboard | F3 | F3 | F3 | F3 | 0x3C |
| 91 | 0x5B | KEY_ID_F4 | keyboard | F4 | F4 | F4 | F4 | 0x3D |
| 92 | 0x5C | KEY_ID_F5 | keyboard | F5 | F5 | F5 | F5 | 0x3E |
| 93 | 0x5D | KEY_ID_F6 | keyboard | F6 | F6 | F6 | F6 | 0x3F |
| 94 | 0x5E | KEY_ID_F7 | keyboard | F7 | F7 | F7 | F7 | 0x40 |
| 95 | 0x5F | KEY_ID_F8 | keyboard | F8 | F8 | F8 | F8 | 0x41 |
| 96 | 0x60 | KEY_ID_F9 | keyboard | F9 | F9 | F9 | F9 | 0x42 |
| 97 | 0x61 | KEY_ID_F10 | keyboard | F10 | F10 | F10 | F10 | 0x43 |
| 98 | 0x62 | KEY_ID_F11 | keyboard | F11 | F11 | F11 | F11 | 0x44 |
| 99 | 0x63 | KEY_ID_F12 | keyboard | F12 | F12 | F12 | F12 | 0x45 |
| 100 | 0x64 | KEY_ID_PSC | keyboard | Print Screen | Print Screen | Print Screen | Print Screen | 0x46 |
| 101 | 0x65 | KEY_ID_SLK | keyboard | Scroll Lock | Scroll Lock | Scroll Lock | Scroll Lock | 0x47 |
| 102 | 0x66 | KEY_ID_PSE | keyboard | Pause | Pause | Pause | Pause | 0x48 |
| 103 | 0x67 | KEY_ID_LWIN | keyboard | Left WIN | Left WIN | Left WIN | Left WIN | 0xE3 |
| 104 | 0x68 | KEY_ID_APP | keyboard | Right Win | Right Win | Right Win | Right Win | 0xE7? (or 0x65) |
| 105 | 0x69 | KEY_ID_ISO | keyboard | \ | \ | < | < | 0x64 |
| 109 | 0x6D | KEY_ID_NUMPAD_B1 | numpad display key | Numpad Image Button 1 | Numpad Image Button 1 | Numpad Image Button 1 | Numpad Image Button 1 | — (binding) |
| 110 | 0x6E | KEY_ID_NUMPAD_B2 | numpad display key | Numpad Image Button 2 | Numpad Image Button 2 | Numpad Image Button 2 | Numpad Image Button 2 | — (binding) |
| 111 | 0x6F | KEY_ID_NUMPAD_B3 | numpad display key | Numpad Image Button 3 | Numpad Image Button 3 | Numpad Image Button 3 | Numpad Image Button 3 | — (binding) |
| 112 | 0x70 | KEY_ID_NUMPAD_B4 | numpad display key | Numpad Image Button 4 | Numpad Image Button 4 | Numpad Image Button 4 | Numpad Image Button 4 | — (binding) |
| 113 | 0x71 | KEY_ID_NUMPAD_B5 | numpad display key | Numpad Image Button 5 | Numpad Image Button 5 | Numpad Image Button 5 | Numpad Image Button 5 | — (binding) |
| 114 | 0x72 | KEY_ID_NUMPAD_B6 | numpad display key | Numpad Image Button 6 | Numpad Image Button 6 | Numpad Image Button 6 | Numpad Image Button 6 | — (binding) |
| 115 | 0x73 | KEY_ID_NUMPAD_B7 | numpad display key | Numpad Image Button 7 | Numpad Image Button 7 | Numpad Image Button 7 | Numpad Image Button 7 | — (binding) |
| 116 | 0x74 | KEY_ID_NUMPAD_B8 | numpad display key | Numpad Image Button 8 | Numpad Image Button 8 | Numpad Image Button 8 | Numpad Image Button 8 | — (binding) |
| 117 | 0x75 | KEY_ID_MUTE | media dock button | Mute | Mute | Mute | Mute | Consumer Mute 0xE2 |
| 118 | 0x76 | KEY_ID_PLAY_PAUSE | media dock button | Play/Pause | Play/Pause | Play/Pause | Play/Pause | Consumer Play/Pause 0xCD |
| 119 | 0x77 | KEY_ID_PREVIOUS_TRACK | media dock button | Previous Track | Previous Track | Previous Track | Previous Track | Consumer Scan Prev 0xB6 |
| 120 | 0x78 | KEY_ID_NEXT_TRACK | media dock button | Next Track | Next Track | Next Track | Next Track | Consumer Scan Next 0xB5 |

### 3.7 HID usage values used by the web app (StandardKey payload) — HIGH
Per-layout lists `Xd[n.*]` (L20376): US/UK/DE/NO use identical usage values (labels differ; DE swaps Y/Z labels), FR (AZERTY)
maps labels to different usages. The US list (65 entries) is below; punctuation keys are not offered as StandardKey — the UI
uses AltCode (type 11) for them.

| HID usage (dec) | hex | UI label (US) | description |
|---:|---:|---|---|
| 4 | 0x04 | A | A |
| 5 | 0x05 | B | B |
| 6 | 0x06 | C | C |
| 7 | 0x07 | D | D |
| 8 | 0x08 | E | E |
| 9 | 0x09 | F | F |
| 10 | 0x0A | G | G |
| 11 | 0x0B | H | H |
| 12 | 0x0C | I | I |
| 13 | 0x0D | J | J |
| 14 | 0x0E | K | K |
| 15 | 0x0F | L | L |
| 16 | 0x10 | M | M |
| 17 | 0x11 | N | N |
| 18 | 0x12 | O | O |
| 19 | 0x13 | P | P |
| 20 | 0x14 | Q | Q |
| 21 | 0x15 | R | R |
| 22 | 0x16 | S | S |
| 23 | 0x17 | T | T |
| 24 | 0x18 | U | U |
| 25 | 0x19 | V | V |
| 26 | 0x1A | W | W |
| 27 | 0x1B | X | X |
| 28 | 0x1C | Y | Y |
| 29 | 0x1D | Z | Z |
| 30 | 0x1E | 1 | Digit One |
| 31 | 0x1F | 2 | Digit Two |
| 32 | 0x20 | 3 | Digit Three |
| 33 | 0x21 | 4 | Digit Four |
| 34 | 0x22 | 5 | Digit Five |
| 35 | 0x23 | 6 | Digit Six |
| 36 | 0x24 | 7 | Digit Seven |
| 37 | 0x25 | 8 | Digit Eight |
| 38 | 0x26 | 9 | Digit Nine |
| 39 | 0x27 | 0 | Digit Zero |
| 40 | 0x28 | Enter | Enter |
| 41 | 0x29 | Esc | Esc |
| 42 | 0x2A | Backspace | Backspace |
| 43 | 0x2B | Tab | Tab |
| 44 | 0x2C | Space | Spacebar |
| 57 | 0x39 | CapsLock | Caps Lock |
| 70 | 0x46 | PrintScreen | Print Screen |
| 71 | 0x47 | ScrollLock | Scroll Lock |
| 72 | 0x48 | Pause/Break | Pause/Break |
| 73 | 0x49 | Insert | Insert |
| 74 | 0x4A | Home | Home |
| 75 | 0x4B | PageUp | Page Up |
| 76 | 0x4C | Del | Delete |
| 77 | 0x4D | End | End |
| 78 | 0x4E | PageDown | Page Down |
| 79 | 0x4F | → | Right Arrow |
| 80 | 0x50 | ← | Left Arrow |
| 81 | 0x51 | ↓ | Down Arrow |
| 82 | 0x52 | ↑ | Up Arrow |
| 83 | 0x53 | NumLock | Num Lock |
| 101 | 0x65 | Menu/Context | Menu/Context |
| 224 | 0xE0 | LeftCtrl | Left Ctrl |
| 225 | 0xE1 | LeftShift | Left Shift |
| 226 | 0xE2 | LeftAlt | Left Alt |
| 227 | 0xE3 | LeftWin | Left Win |
| 228 | 0xE4 | RightCtrl | Right Ctrl |
| 229 | 0xE5 | RightShift | Right Shift |
| 230 | 0xE6 | RightAlt | Right Alt |
| 231 | 0xE7 | RightWin | Right Win |

**F-keys (`Jd`, L20303) — sent as StandardKey with modifiers = 0 (`fKey`):**

F1=58 (0x3A), F2=59 (0x3B), F3=60 (0x3C), F4=61 (0x3D), F5=62 (0x3E), F6=63 (0x3F), F7=64 (0x40), F8=65 (0x41), F9=66 (0x42), F10=67 (0x43), F11=68 (0x44), F12=69 (0x45), F13=104 (0x68), F14=105 (0x69), F15=106 (0x6A), F16=107 (0x6B), F17=108 (0x6C), F18=109 (0x6D), F19=110 (0x6E), F20=111 (0x6F), F21=112 (0x70), F22=113 (0x71), F23=114 (0x72), F24=115 (0x73)

**AltCode / "Special" characters (`_d`, L21807; type 11, value = Unicode code point u16 LE):**

`À`=192 · `Á`=193 · `Â`=194 · `Ã`=195 · `Ä`=196 · `Å`=197 · `Æ`=198 · `Ç`=199 · `È`=200 · `É`=201 · `Ê`=202 · `Ë`=203 · `Ì`=204 · `Í`=205 · `Î`=206 · `Ï`=207 · `Ð`=208 · `Ñ`=209 · `Ò`=210 · `Ó`=211 · `Ô`=212 · `Õ`=213 · `Ö`=214 · `×`=215 · `Ø`=216 · `Ù`=217 · `Ú`=218 · `Û`=219 · `Ü`=220 · `Ý`=221 · `Þ`=222 · `÷`=247 · `!`=33 · `#`=35 · `$`=36 · `%`=37 · `&`=38 · `'`=39 · `(`=40 · `)`=41 · `*`=42 · `+`=43 · `,`=44 · `-`=45 · `.`=46 · `/`=47 · `:`=58 · `;`=59 · `<`=60 · `=`=61 · `>`=62 · `?`=63 · `@`=64 · `[`=91 · `\`=92 · `]`=93 · `^`=94 · `_`=95 · (backtick)=96 · `{`=123 · `}`=125 · `~`=126 · `€`=8364 · `‚`=8218 · `ƒ`=402 · `„`=8222 · `…`=8230 · `†`=8224 · `‡`=8225 · `ˆ`=710 · `‰`=8240 · `Š`=352 · `‹`=8249 · `Œ`=338 · `Ž`=381 · `‘`=8216 · `’`=8217 · `“`=8220 · `”`=8221 · `•`=8226 · `–`=8211 · `—`=8212 · `˜`=732 · `™`=8482 · `š`=353 · `›`=8250 · `œ`=339 · `ž`=382 · `Ÿ`=376 · `¡`=161 · `¢`=162 · `£`=163 · `¤`=164 · `¥`=165 · `¦`=166 · `§`=167 · `¨`=168 · `©`=169 · `ª`=170 · `«`=171 · `¬`=172 · `®`=174 · `¯`=175 · `°`=176 · `±`=177 · `²`=178 · `³`=179 · `´`=180 · `µ`=181 · `¶`=182 · `·`=183 · `¸`=184 · `¹`=185 · `º`=186 · `»`=187 · `¼`=188 · `½`=189 · `¾`=190 · `¿`=191 · `ß`=223

---

## 4. MACROS (feature 18)

### 4.1 Commands
| Cmd | Name | Request | Response | Conf |
|---|---|---|---|---|
| 1 | GetMacros | ? | ? | LOW |
| 2 | SetMacros | ? | ? | LOW |
| 3 | GetMacroConfig | ? | ? | LOW |
| 4 | SetMacroConfig | ? | ? | LOW |
| 5 | GetMacroEvents | ? | ? | LOW |
| 6 | SetMacroEvents | ? | ? | LOW |
| 7 | GetMacroName | ? | ? | LOW |
| 8 | SetMacroName | ? | ? | LOW |
| 9 | StartRecording | `[n][eventType…]` | (web reads `data[1]`) | HIGH (`Uie` L60599) |
| 10 | StopRecording | — | — | HIGH (`Bie` L60614) |
| 11 | AbortPlayback | ? | ? | LOW |

```js
// Uie (L60606)
createTransmitPacket(SID, j.MACROS, ns.StartRecording, [this.row.length, ...this.row])
// callers: StartObserve_InputEvent([F3.PhysicalKeyPress]) (L16249, L35136, L53737) ; StopObserve_InputEvent() on cleanup
```

The web app uses StartRecording purely as a **live key observer** (Reactive-effect preview, Quick-Tap key picker). Macro
storage, event format, limits (count, events, name length) and playback modes are **not in the bundle** — the macro
editor is desktop-only (L2688 "Macro editor"), and desktop `.ioprofile` imports carry no macro property (only
KeyBindings/Lightings/MediaDock/Numpad/… properties, L26877–L26904).

### 4.2 Input-event types (`F3`, L10244) — HIGH values
| id | name | id | name |
|---|---|---|---|
| 1 | ModifiersStateChange | 8 | **PhysicalKeyPress** |
| 2 | StandardKeyPress | 9 | **PhysicalKeyRelease** |
| 3 | StandardKeyRelease | 10 | ButtonsStateChange |
| 4 | PowerKeyPress | 11 | VerticalScroll |
| 5 | PowerKeyRelease | 12 | HorizontalScroll |
| 6 | ConsumerKeyPress | 13 | PointerMovement |
| 7 | ConsumerKeyRelease | | |

These are almost certainly also the macro event types (the recorder captures them) — MED.

### 4.3 InputEvent notification (MACROS notif 5) — HIGH (bytes 0–1), MED (byte 2)
```
data[0] = event type (F3)   data[1] = keyId (x table, low byte)   data[2] = keyId high byte (MED)
```
```js
// Kn.onInputEventHandle (L16956)  key: A.data[1]
// K3 onInputEventHandle (L23786)  if (A.data[0] === 9) return;   // ignore PhysicalKeyRelease
```
The observed `08 2D 00` / `09 2D 00` = PhysicalKeyPress / PhysicalKeyRelease of key id 0x2D = 45 = `KEY_ID_c` (the "C" key).
Events only flow after some session sent StartRecording (the web app subscribes to `[8]` only; the K3 handler's explicit
release filter suggests the firmware may send releases anyway, or the desktop app subscribed to `[8, 9]`). Send
StopRecording when done.

### 4.4 Link to bindings
Binding type 10 (§3.3): `[keyId][layer][0x0A][action][macroId…]`. Width of macroId (u8 on read vs u16 on write) and the
meaning of `action` are unresolved — LOW. Capture the desktop macro editor to fill §4.1.

---

## 5. NUMPAD_MODULE (32) and MEDIA_DOCK_MODULE (33) — extras

* The numpad module is a **full numeric keypad** (`PAD_ID_*` 70–86, normal bindable keys, also drawn on the lighting page)
  **plus** the 8 display keys B1–B8 (109–116, only drawn on the key-binding page and only when the numpad is connected →
  no RGB). It attaches **Left or Right** (`nA` 1/2) — the SVG draws it on either side (§8). HIGH.
* Numpad/Dock **StateChanged** notifications: `[state][position]`, or `[?][state][position]` when `data.length > 2`
  (L16922–L16955). On DeviceInfoChanged the web app re-reads GetDeviceInfo, dock and numpad state (L16900). HIGH.
* No per-key display settings besides the image; image format per QLINK_PROTOCOL.md §4.3. Factory images are embedded
  in the bundle (`q3`) and re-uploaded on "reset remap". HIGH.
* Dock: see §0.2 item 4 for enable flags; CUSTOM mode §3.5; `Get/SetProfileInfo`, `Get/SetPlaybackInfo` desktop-only.
* Firmware gating: `_mandatoryUpdateCheck(1, 13, 0)` (L16851) — any connected MCU (0 keyboard, 1 dock, 2 numpad) below
  1.13.0 triggers the mandatory-update flow; `Keyboard_GetState` needs keyboard MCU ≥ 1.2.0. HIGH.

---

## 6. USB_DEVICE (feature 6) polling rate — HIGH format, **risk**
| Cmd | Request | Response |
|---|---|---|
| 1 GetPollingRate | — | `[rate u16 LE]` in Hz (`Sne` L59063: `data[1] << 8 \| data[0]`) |
| 2 SetPollingRate | `[rate u16 LE]` Hz (`Dne` L59077) | — |

Values in the bundle (mouse dropdown `x$` L52317): 125, 250, 500, 1000, 2000, 4000, 8000. Keyboards: `Kn` default 1000,
read on connect only; there is **no keyboard UI** — `profile/updatePollingRate` (L28293) is never dispatched. A profile import
that contains a different `settings.PollingRate` *would* send SetPollingRate (L61040). USB_DEVICE is not in `Kn.Features`
(only present if GetSupportedFeatures lists it). Risk: never exercised by the web app on this keyboard; may re-enumerate the
USB device or be rejected (INVALID_PARAMETER). Treat as read-only.

---

## 7. Profiles — MED
* **The keyboard stores exactly one active configuration** (lighting mode/layer, bindings, lock mask, dock config, images).
  Evidence: the web app reads everything back from the device on connect (L16836); the redux `profileReducer` is *not*
  persisted (only images, favourite colours, language … are wrapped in redux-persist, L32898–L32990); a fresh "Profile 1" is
  created each session from device data (L63321). No QLink command selects/stores a profile on the keyboard.
* Profile *switching* is host-side: binding type 8 (Profile) and MEDIA_DOCK Get/SetProfileInfo (dock shows the active
  profile name) are desktop-only.
* Web export: `io_center_web_profile.webioprofile` = JSON `{version:1, platform:"web", profiles, numpadImageBindings,
  mediaDockCurrentImage, mediaDockCustomImages, numpadCustomImages, favoriteColors, …}` (L68052). Import also accepts desktop
  `.ioprofile` = ZIP with `data` (cereal JSON, polymorphic `KeyBindingsProperty`, `LightingsProperty`, `MediaDockProperty`,
  `NumpadProperty`, …) plus `media_dock*` / `numpad*` image files (L68109, converter `PV` L26863).
* **"Apply profile" wire sequence** (`YN` L27811 → 1 s delay → `protocol.setConfig` L60998, only if session Active; each
  changed path produces one command, all pushed through the serial queue in object-key order):
  1. KEYBOARD **SetConfig** `[lockMask]` (if `keyConfig` changed)
  2. LIGHTINGS **SetLightingMode** `[0|1]` (if enabled changed), **SetLayerConfig** `[0, …]` (if General changed)
  3. USB_DEVICE **SetPollingRate** (if changed!)
  4. BINDINGS: for every changed entry of either layer **ClearBinding** `[key][layer]` (type Default) or **SetBinding**;
     entries present on the device but absent from the profile are turned into Default → ClearBinding (L27849)
  5. BINDINGS **SetConfig** `[enabled]` (if changed)
  6. MEDIA_DOCK **SetConfig** (9 bytes) (if changed and dock connected)
  7. then, for the Dark Mount: 8 × numpad image upload (only for keys that have an image in the profile), then the dock
     screensaver image (type 0).

---

## 8. Key geometry for a per-key editor (ANSI US) — HIGH
Source: SVG overlay component `vz` (L42081–L42902, Dark Mount ANSI; ISO variant `Dp` L42923–L43745), drawn over
`/assets/Project-2-ANSI-{Left,Right}-*.webp`. Each key is an SVG `path` with `id = keyId`; the values below are the path
bounding boxes (rounded-rect keys; `PAD_ID_PLUS` and `PAD_ID_ENT` are 2-row keys).

Coordinate system: layout units of the SVG. The canvas is `2649 × 886` when the numpad is attached (`2108 × 886` without);
the whole layout group is drawn with `translate(0,0)` when the numpad is on the **Left** and `translate(-540,0)` when it is on
the **Right** or absent. Main-block keys have fixed coordinates; numpad keys have two x positions (Left: x ≈ 69–475,
Right: x ≈ 2718–3123, i.e. Left + 2649). Key pitch ≈ 110 units; a 1u key ≈ 72 × 78.

ISO differences (from `Dp`): `KEY_ID_RTN` becomes a 2-row key (2102, 430, 126 × 190), `KEY_ID_BSL` moves to (2018, 540, 74 × 78)
(ISO "#"), `KEY_ID_LSHFT` shrinks to (607, 647, 102 × 78) and `KEY_ID_ISO` appears at (747, 648, 72 × 78); other keys ±2 units.

| id | name | x | y | w | h |
|---:|---|---:|---:|---:|---:|
| 87 | KEY_ID_ESC | 607 | 188 | 72 | 78 |
| 88 | KEY_ID_F1 | 830 | 188 | 72 | 78 |
| 89 | KEY_ID_F2 | 941 | 188 | 70 | 78 |
| 90 | KEY_ID_F3 | 1051 | 188 | 72 | 78 |
| 91 | KEY_ID_F4 | 1162 | 188 | 70 | 78 |
| 92 | KEY_ID_F5 | 1328 | 188 | 72 | 78 |
| 93 | KEY_ID_F6 | 1438 | 188 | 72 | 78 |
| 94 | KEY_ID_F7 | 1549 | 187 | 72 | 78 |
| 95 | KEY_ID_F8 | 1660 | 187 | 72 | 78 |
| 96 | KEY_ID_F9 | 1825 | 187 | 70 | 78 |
| 97 | KEY_ID_F10 | 1933 | 187 | 74 | 78 |
| 98 | KEY_ID_F11 | 2048 | 187 | 72 | 78 |
| 99 | KEY_ID_F12 | 2157 | 187 | 72 | 78 |
| 100 | KEY_ID_PSC | 2292 | 186 | 72 | 78 |
| 101 | KEY_ID_SLK | 2403 | 186 | 70 | 78 |
| 102 | KEY_ID_PSE | 2514 | 186 | 72 | 78 |
| 1 | KEY_ID_TIL | 607 | 321 | 72 | 78 |
| 2 | KEY_ID_1 | 718 | 321 | 72 | 78 |
| 3 | KEY_ID_2 | 829 | 321 | 70 | 78 |
| 4 | KEY_ID_3 | 940 | 321 | 70 | 78 |
| 5 | KEY_ID_4 | 1049 | 321 | 74 | 78 |
| 6 | KEY_ID_5 | 1162 | 321 | 70 | 78 |
| 7 | KEY_ID_6 | 1273 | 321 | 70 | 78 |
| 8 | KEY_ID_7 | 1384 | 321 | 70 | 78 |
| 9 | KEY_ID_8 | 1493 | 321 | 72 | 78 |
| 10 | KEY_ID_9 | 1604 | 321 | 72 | 78 |
| 11 | KEY_ID_0 | 1715 | 321 | 72 | 78 |
| 12 | KEY_ID_MIS | 1824 | 321 | 74 | 78 |
| 13 | KEY_ID_EQU | 1935 | 321 | 73 | 78 |
| 14 | KEY_ID_BSP | 2047 | 321 | 182 | 78 |
| 60 | KEY_ID_INS | 2293 | 320 | 72 | 78 |
| 63 | KEY_ID_HOME | 2402 | 320 | 72 | 78 |
| 67 | KEY_ID_PUP | 2515 | 320 | 72 | 78 |
| 15 | KEY_ID_TAB | 608 | 428 | 128 | 78 |
| 16 | KEY_ID_q | 774 | 428 | 74 | 78 |
| 17 | KEY_ID_w | 884 | 428 | 74 | 78 |
| 18 | KEY_ID_e | 994 | 428 | 74 | 78 |
| 19 | KEY_ID_r | 1104 | 428 | 74 | 78 |
| 20 | KEY_ID_t | 1214 | 428 | 74 | 78 |
| 21 | KEY_ID_y | 1327 | 428 | 72 | 78 |
| 22 | KEY_ID_u | 1437 | 428 | 74 | 78 |
| 23 | KEY_ID_i | 1547 | 428 | 74 | 78 |
| 24 | KEY_ID_o | 1658 | 428 | 74 | 78 |
| 25 | KEY_ID_p | 1768 | 428 | 74 | 78 |
| 26 | KEY_ID_OQO | 1878 | 428 | 74 | 78 |
| 27 | KEY_ID_EQO | 1990 | 428 | 74 | 78 |
| 28 | KEY_ID_BSL | 2101 | 428 | 128 | 78 |
| 61 | KEY_ID_DEL | 2293 | 427 | 72 | 80 |
| 64 | KEY_ID_END | 2402 | 427 | 72 | 80 |
| 68 | KEY_ID_PDN | 2514 | 427 | 72 | 78 |
| 29 | KEY_ID_CAP | 608 | 539 | 156 | 78 |
| 30 | KEY_ID_a | 802 | 539 | 74 | 78 |
| 31 | KEY_ID_s | 912 | 539 | 74 | 78 |
| 32 | KEY_ID_d | 1023 | 539 | 73 | 78 |
| 33 | KEY_ID_f | 1134 | 539 | 72 | 78 |
| 34 | KEY_ID_g | 1243 | 539 | 74 | 78 |
| 35 | KEY_ID_h | 1356 | 539 | 71 | 78 |
| 36 | KEY_ID_j | 1465 | 539 | 72 | 78 |
| 37 | KEY_ID_k | 1575 | 539 | 74 | 78 |
| 38 | KEY_ID_l | 1685 | 539 | 74 | 78 |
| 39 | KEY_ID_COL | 1796 | 539 | 74 | 78 |
| 40 | KEY_ID_CC | 1907 | 539 | 74 | 78 |
| 41 | KEY_ID_RTN | 2016 | 539 | 213 | 78 |
| 42 | KEY_ID_LSHFT | 609 | 648 | 213 | 78 |
| 43 | KEY_ID_z | 858 | 648 | 72 | 78 |
| 44 | KEY_ID_x | 968 | 648 | 74 | 78 |
| 45 | KEY_ID_c | 1080 | 648 | 72 | 78 |
| 46 | KEY_ID_v | 1190 | 648 | 72 | 78 |
| 47 | KEY_ID_b | 1301 | 648 | 72 | 78 |
| 48 | KEY_ID_n | 1411 | 648 | 72 | 78 |
| 49 | KEY_ID_m | 1522 | 648 | 72 | 78 |
| 50 | KEY_ID_CMA | 1632 | 648 | 72 | 78 |
| 51 | KEY_ID_DOT | 1742 | 648 | 72 | 78 |
| 52 | KEY_ID_SL | 1853 | 648 | 72 | 78 |
| 53 | KEY_ID_RSHFT | 1965 | 645 | 266 | 78 |
| 65 | KEY_ID_UA | 2403 | 647 | 72 | 78 |
| 54 | KEY_ID_LCTRL | 608 | 758 | 102 | 78 |
| 103 | KEY_ID_LWIN | 747 | 758 | 102 | 78 |
| 56 | KEY_ID_LALT | 885 | 758 | 102 | 78 |
| 57 | KEY_ID_SPC | 1023 | 756 | 652 | 78 |
| 58 | KEY_ID_RALT | 1715 | 757 | 102 | 78 |
| 104 | KEY_ID_APP | 1853 | 757 | 102 | 78 |
| 55 | KEY_ID_FN | 1992 | 757 | 102 | 78 |
| 59 | KEY_ID_RCTRL | 2129 | 756 | 102 | 78 |
| 62 | KEY_ID_LA | 2293 | 757 | 72 | 78 |
| 66 | KEY_ID_DA | 2403 | 757 | 72 | 78 |
| 69 | KEY_ID_RA | 2514 | 757 | 72 | 78 |

| id | name | x (numpad Left) | x (numpad Right) | y | w | h |
|---:|---|---:|---:|---:|---:|---:|
| 109 | KEY_ID_NUMPAD_B1 | 76 | 2724 | 60 | 84 | 84 |
| 110 | KEY_ID_NUMPAD_B2 | 178 | 2827 | 60 | 84 | 84 |
| 111 | KEY_ID_NUMPAD_B3 | 280 | 2930 | 60 | 84 | 84 |
| 112 | KEY_ID_NUMPAD_B4 | 382 | 3032 | 60 | 84 | 84 |
| 113 | KEY_ID_NUMPAD_B5 | 76 | 2724 | 178 | 84 | 84 |
| 114 | KEY_ID_NUMPAD_B6 | 178 | 2827 | 178 | 84 | 84 |
| 115 | KEY_ID_NUMPAD_B7 | 280 | 2930 | 178 | 84 | 84 |
| 116 | KEY_ID_NUMPAD_B8 | 382 | 3032 | 178 | 84 | 84 |
| 70 | PAD_ID_NUM | 69 | 2718 | 321 | 72 | 78 |
| 74 | PAD_ID_SL | 179 | 2828 | 321 | 75 | 78 |
| 79 | PAD_ID_MUL | 292 | 2941 | 321 | 72 | 78 |
| 84 | PAD_ID_SUB | 403 | 3051 | 321 | 72 | 78 |
| 71 | PAD_ID_7 | 69 | 2718 | 429 | 72 | 80 |
| 75 | PAD_ID_8 | 179 | 2828 | 429 | 74 | 80 |
| 80 | PAD_ID_9 | 291 | 2940 | 429 | 74 | 80 |
| 85 | PAD_ID_PLUS | 403 | 3051 | 429 | 72 | 191 |
| 72 | PAD_ID_4 | 69 | 2718 | 538 | 72 | 82 |
| 76 | PAD_ID_5 | 180 | 2829 | 538 | 72 | 82 |
| 81 | PAD_ID_6 | 292 | 2940 | 540 | 73 | 80 |
| 73 | PAD_ID_1 | 69 | 2718 | 649 | 72 | 78 |
| 77 | PAD_ID_2 | 179 | 2828 | 649 | 74 | 78 |
| 82 | PAD_ID_3 | 292 | 2941 | 649 | 72 | 78 |
| 86 | PAD_ID_ENT | 401 | 3051 | 649 | 72 | 188 |
| 78 | PAD_ID_0 | 70 | 2718 | 759 | 182 | 78 |
| 83 | PAD_ID_DEL | 292 | 2941 | 759 | 72 | 78 |

Media-dock buttons are not in the SVG; they are drawn as a 2 × 2 HTML button grid (`tB` L42018): Top-Left 117 Mute,
Top-Right 118 Play/Pause, Bottom-Left 119 Previous, Bottom-Right 120 Next.

---

## 9. Dangerous / irreversible commands in these features

| Feature / cmd | Why | Conf |
|---|---|---|
| LIGHTINGS 16 **SetCalibration** | overwrites factory RGB white-balance gains | HIGH |
| DEVICE_INFO 5 **FactoryReset** `[len][serial ASCII]` (`Xte` L58457) | wipes bindings, lighting, dock config, images; triggered by the web "reset settings" button **and after every layout change** | HIGH |
| raw `writeReport(0, [FF FE 00 layout])` (L16003) | changes visual layout outside QLink; web follows with FactoryReset | HIGH |
| USB_DEVICE 2 **SetPollingRate** | never exercised on keyboards; possible re-enumeration | MED |
| LIGHTINGS 4 SetLayersLayout, 8 SetLayerMask, 10 SetLayerName, 12 SetPlaybackState, 13 PerformRealtimeUpdates | desktop "Custom/Realtime" machinery with unknown payloads; wrong bytes may corrupt stored custom layers | LOW |
| MACROS 2/4/6/8 (Set…), 11 AbortPlayback | unknown storage format; could corrupt macro store | LOW |
| KEYBOARD 7 SetSnapTapConfig on the Dark Mount | support unknown (K3 feature); "restricted or bannable" in some games (L3288) | LOW |
| BINDINGS reset-to-default loop | many flash writes (+ 8 image uploads); harmless but slow | MED |
| Key combo **Fn + R held 5 s** (physical) | factory reset | HIGH |

Not dangerous but side-effecting: MACROS 9 StartRecording (streams notifications until StopRecording);
LIGHTINGS SetLightingMode(1) on connect (kicks desktop Custom/Realtime lighting); SetLayerConfig/SetBinding are persistent.

---

## 10. Biggest gaps / how to close them
1. **Per-key RGB** (LIGHTINGS 7/8/11/12/13, mode 2/3): no payloads in the bundle. First check for a HID LampArray
   collection (read-only); else USB-capture the desktop app editing a Custom layer / using Windows Dynamic Lighting.
2. **Macros** (MACROS 1–8, 11): capture the desktop macro editor (record a 2-key macro, bind it, play it).
3. **GetBindings header** (`a`,`b` bytes) and paging behaviour on the Dark Mount — read-only, safe to test.
4. **Brightness scale** (0–100 vs 0–255) — read GetLayerConfig after a web-app change.
5. **Binding type 8/10 widths** (profile UUID vs u16, macroId u8 vs u16) — only matters for desktop-created bindings; read
   them back from a device configured by the desktop app.
6. **ActionFired** payload and who executes OpenFile/OpenBrowser/Profile actions — observe notifications with IO Center closed.
7. Quick-Tap support on the Dark Mount — GetSnapTapConfig is read-only and safe to probe (expect NOT_SUPPORTED = 13 if absent).

---

## Appendix A — example request packets (SID 2, locally computed with the reference encoder, NOT sent)
Format: `bytes 0..LEN` | zero padding | CRC-16/MODBUS (LE) at [62..63]. REQUEST_ID increments per packet.

```
### KEYBOARD GetConfig
06 00 02 00 0a 07 02 | 00 x55 | crc 57 03
### KEYBOARD SetConfig (disable Win + Alt+Tab in game mode = 0x0C)
07 00 02 00 0b 07 03 0c | 00 x54 | crc 51 9e
### KEYBOARD GetState
06 00 02 00 0c 07 04 | 00 x55 | crc 44 23
### KEYBOARD SetState (game mode on, quick tap off = 0x01)
07 00 02 00 0d 07 05 01 | 00 x54 | crc 17 b6
### KEYBOARD GetLayout
06 00 02 00 0e 07 01 | 00 x55 | crc 56 c3
### LIGHTINGS GetLightingMode
06 00 02 00 0f 10 01 | 00 x55 | crc 14 29
### LIGHTINGS SetLightingMode General(1)
07 00 02 00 10 10 02 01 | 00 x54 | crc 91 23
### LIGHTINGS GetLayerConfig layer 0
07 00 02 00 11 10 05 00 | 00 x54 | crc da 77
### LIGHTINGS SetLayerConfig L0 Static, dir 0, bright 100, speed 50, single FF2800
0f 00 02 00 12 10 06 00 00 00 64 32 00 ff 28 00 | 00 x46 | crc 27 c0
### LIGHTINGS SetLayerConfig L0 ColorWave, Right, bright 80, speed 50, gradient 3 stops
19 00 02 00 13 10 06 00 01 03 50 32 02 03 ff 00 00 00 00 ff 00 32 00 00 ff 64 | 00 x36 | crc 97 ff
### LIGHTINGS SetLayerConfig L0 Reactive, dir 0, bright 100, speed 50, dual FF2800/FFFFFF
12 00 02 00 14 10 06 00 04 00 64 32 01 ff 28 00 ff ff ff | 00 x43 | crc 6c d1
### BINDINGS GetConfig
06 00 02 00 15 11 04 | 00 x55 | crc 12 4a
### BINDINGS SetConfig enabled
07 00 02 00 16 11 05 01 | 00 x54 | crc 47 ff
### BINDINGS GetBindings from index 0
08 00 02 00 17 11 01 00 00 | 00 x53 | crc 04 24
### BINDINGS SetBinding Common CapsLock(29) -> Esc (HID 0x29)
0b 00 02 00 18 11 02 1d 00 01 00 29 | 00 x50 | crc f1 d8
### BINDINGS SetBinding Common B1(109) -> Ctrl+Shift+Esc (mods 0x03, HID 0x29)
0b 00 02 00 19 11 02 6d 00 01 03 29 | 00 x50 | crc 32 47
### BINDINGS SetBinding Fn F1(88) -> Media Mute(3)
0a 00 02 00 1a 11 02 58 80 02 03 | 00 x51 | crc 21 88
### BINDINGS SetBinding Common LWIN(103) -> Disable (type byte 0)
09 00 02 00 1b 11 02 67 00 00 | 00 x52 | crc b4 98
### BINDINGS SetBinding Common B8(116) -> WindowsShortcut Calculator(2)
0a 00 02 00 1c 11 02 74 00 07 02 | 00 x51 | crc 3e 96
### BINDINGS SetBinding Common B2(110) -> Mouse Left, AutoFire 20/s
0c 00 02 00 1d 11 02 6e 00 03 01 00 14 | 00 x49 | crc 02 45
### BINDINGS SetBinding Fn UA(65) -> Backlight IncreaseBrightness(4)
0a 00 02 00 1e 11 02 41 80 09 04 | 00 x51 | crc bf da
### BINDINGS SetBinding Common B3(111) -> AltCode "€" (0x20AC)
0c 00 02 00 1f 11 02 6f 00 0b 02 ac 20 | 00 x49 | crc e4 00
### BINDINGS SetBinding Common B4(112) -> OpenBrowser "https://a.b"
15 00 02 00 20 11 02 70 00 06 0b 68 74 74 70 73 3a 2f 2f 61 2e 62 | 00 x40 | crc 92 82
### BINDINGS ClearBinding Common CapsLock(29)
08 00 02 00 21 11 03 1d 00 | 00 x53 | crc 52 49
### BINDINGS ClearBinding Fn F1(88)
08 00 02 00 22 11 03 58 80 | 00 x53 | crc 33 d4
### MACROS StartRecording [PhysicalKeyPress]
08 00 02 00 23 12 09 01 08 | 00 x53 | crc 47 70
### MACROS StartRecording [PhysicalKeyPress, PhysicalKeyRelease]
09 00 02 00 24 12 09 02 08 09 | 00 x52 | crc e5 84
### MACROS StopRecording
06 00 02 00 25 12 0a | 00 x55 | crc c3 f0
### USB_DEVICE GetPollingRate
06 00 02 00 26 06 01 | 00 x55 | crc 17 80
```
