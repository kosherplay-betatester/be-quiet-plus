# QLink protocol (be quiet! IO Center Web) — Dark Mount keyboard

Source: `scratchpad/web/index.js` (minified), beautified to `scratchpad/web/pretty.js` (js-beautify).
All line numbers below refer to `pretty.js`. A reference encoder (no device I/O) is in
`scratchpad/web/qlink_ref.js`.

Confidence scale: **HIGH** = read directly from code; **MED** = strong inference from code;
**LOW** = guess / not exercised by the web app for this device.

---

## 0. Device identification

| Item | Value | Where |
|---|---|---|
| VID | 0x373F (14143) | `Hs.VidPid` L60835 |
| Dark Mount runtime | PID 0x0001 → class `Kn` (deviceType `P200`) | `case 926875649: return new Kn(A,i)` (0x373F0001) L62847 |
| Dark Mount **bootloader** | PID 0x0009 → class `KB extends Cc` (`isBootloader = !0`, features ROOT+DFU only) | `case 926875657: return new KB` L62846, class L58205 |
| HID interface filter | `collections[0].usagePage === 65280` (0xFF00) and has output/feature reports | `H6.validate` L15385 |
| Packet size | `outputReports[0].items[0].reportCount ?? 64` → 64 for Dark Mount | `ec.createProtocol(...)` L62844 |
| Report ID | **0** (`getReportId(){return 0}`) | L58366 |
| MCU index in DeviceInfo | 0 = keyboard, 1 = media dock, 2 = numpad | `_checkDeviceModulesUpdate` L16007 |
| Declared features (P200) | ROOT, DEVICE_INFO, KEYBOARD, LIGHTINGS, BINDINGS, NUMPAD_MODULE, MEDIA_DOCK_MODULE (+ whatever `GetSupportedFeatures` returns) | L16600 |

Confidence: HIGH.

On Windows (HidD / WriteFile) write 65 bytes: `0x00` report ID + 64-byte packet. Input reports arrive as 64 bytes (+ report ID 0 on Windows ReadFile).

---

## 1. Transport framing

### 1.1 64-byte packet layout ("small" mode, PACKET_SIZE ≤ 254) — HIGH

```js
// L10167
const Wl = 64;
var ri = { PAYLOAD_LENGTH:0, SEQUENCE_ID:1, SESSION_ID:2, STATUS:3, REQUEST_ID:4,
           FEATURE_ID:5, COMMAND_ID:6, DATA:7, CHECK_SUM: Wl-2 /*62*/ };
```

First frame (seq 0 / single frame):

| Byte | Field | Notes |
|---|---|---|
| 0 | LENGTH | `data.length + 6` (counts bytes 1..6 + data) |
| 1 | SEQ | bit7 = "more frames follow", bits0-6 = frame index. `0` for single-frame messages |
| 2 | SESSION_ID | 0 for OpenSession, afterwards the SID returned by the device |
| 3 | STATUS | 0 in requests; response status code (table below) |
| 4 | REQUEST_ID | host counter 1..255 (wraps, skips 0). `0` + seq `0` ⇒ unsolicited **notification** |
| 5 | FEATURE_ID | see §3 |
| 6 | COMMAND_ID | see §3 |
| 7..61 | DATA | max 55 bytes in the first frame |
| 62..63 | CRC16 | little-endian |

Continuation frames (seq ≥ 1): `[LEN][SEQ][SESSION][DATA … up to 59 bytes @3..61][CRC lo][CRC hi]`.

```js
// Oa.create, L58287 (de-minified)
static create(session, reqId, feature, cmd, data, size=64, seq=0, total=1) {
  const p = new Uint8Array(size);
  let maxData = size - 9 + (seq === 0 ? 0 : 4);         // 55 first frame, 59 continuation
  p[0] = (data?.length || 0) + 6;                          // NOTE: +6 even for continuation frames
  p[1] = total > 1 ? seq + ((total-1 === seq ? 0 : 1) << 7) : 0;
  p[2] = session;
  if (seq === 0) { p[3] = 0 /*SUCCESS*/; p[4] = reqId; p[5] = feature; p[6] = cmd; }
  if (data?.length && maxData >= data.length) p.set(data, seq === 0 ? 7 : 3);
  p.set(un(ju(p.slice(0, size-2))), size-2);             // CRC over bytes 0..61, LE
  return new Oa(p, size, data);
}
```

Parsing side: `_data = buf.slice(seq>0 ? 3 : 7, LENGTH+1)` → the device's convention for continuation frames is
`LENGTH = data + 2` (seq+session). The "large" (>254 byte, 16-bit length at [0..1]) variant subtracts 4 for continuation frames;
the small variant does not — looks like a latent web-app bug that is never exercised on this keyboard (all its commands fit in one frame). MED.

Large mode (`_n`, PACKET_SIZE > 254, used only by LCD/DISPLAY_UNIT products with 1024-byte reports): `[LEN lo][LEN hi][SEQ][SESSION][STATUS][REQ][FEAT][CMD][DATA…][CRC lo][CRC hi]`. Not used by Dark Mount.

### 1.2 CRC — HIGH

```js
// L14068
ju = e => { let A = 65535, t;
  for (let i = 0; i < e.length; i++) { A ^= e[i];
    for (let r = 0; r < 8; r++) t = A & 1, A >>= 1, t && (A ^= 40961 /*0xA001*/); }
  return A; }
un = e => [e & 255, e >> 8 & 255]      // little-endian
```

= **CRC-16/MODBUS**: poly 0x8005 reflected (0xA001), init 0xFFFF, refin/refout true, xorout 0. Check value "123456789" → 0x4B37 (verified in `qlink_ref.js`).
Computed over bytes **0..61** of the 64-byte packet (zero padding included), stored at [62]=lo, [63]=hi.
The web app does **not** verify CRC on received packets.

### 1.3 Status codes (`re`, L10169) — HIGH
0 SUCCESS, 1 INVALID_SESSION_ID, 2 INVALID_FEATURE_ID, 3 INVALID_COMMAND_ID, 4 INVALID_REQUEST_ID, 5 INVALID_PARAMETER,
6 TIMEOUT, 7 BUSY, 8 NOT_AUTHORIZED, 9 NOT_ACTIVE, 10 INVALID_STATE, 11 INVALID_SIZE, 12 INSUFFICIENT_RESOURCES, 13 NOT_SUPPORTED.

### 1.4 Fragmentation, ACK, retries, timeouts — HIGH (code), MED (device behaviour)

```js
// eA.sendReadCommand, L58370
if (pkt.payload.length > pkt.maxPayloadSize) {             // > 55 bytes
  const total = Math.ceil(payload.length / maxPayloadSize); let d = 0;
  for (let u = 0; u < payload.length; u += maxPayloadSize + (d > 1 ? 4 : 0)) {
    const len = Math.min(maxPayloadSize + (d > 0 ? 4 : 0), payload.length - u);
    const m = Oa.create(sess, reqId, feat, cmd, payload.slice(u, u+len), SIZE, d, total);
    await target.writeReport(0, m.buffer); d++;            // no ACK between fragments
  }
} else await target.writeReport(0, pkt.buffer);
let r = await target.readReport(); i = Oa.fromBuffer(r);
while (!(i.feature === pkt.feature && i.command === pkt.command)) { r = await readReport(); i = Oa.fromBuffer(r); }
if (i.sequenceId > 0 || i.hasMoreFrame > 0)                // multi-frame response: concatenate data
  for (let more = true; more;) { const u = Oa.fromBuffer(await readReport()); i.data = concat(i.data, u.data); if (!u.hasMoreFrame) more = false; }
```

* One request → exactly one response (same FEATURE/COMMAND; REQUEST_ID is **not** checked). Responses with `STATUS != 0` make the command throw.
* Notifications (`REQUEST_ID==0 && SEQ==0`) are routed by `"feature:command"` to handlers in `oninputreport` (L15323) and never reach `readReport`.
* Commands are serialized by a queue `V5` with **1000 ms per-command timeout** (`qte = 1e3`, L58230; `executeWithTimeout` rejects "Execute timeout").
* No protocol-level retry. Only `writeReport` retries opening the HID device (5×). `DisplayUnit_SetMediaData` retries a chunk up to 3× (not used by the keyboard).

---

## 2. Session handling

### 2.1 Commands (FEATURE ROOT = 1) — HIGH

| Cmd | Name | Request data | Response data |
|---|---|---|---|
| 1 | OpenSession | `[connId u32 LE (random 10000..99999)] [clientType u8]` (SESSION byte = 0) | `[connId u32][SID][state][timeout_s]` |
| 2 | CloseSession | `[SID]` | — |
| 3 | KeepAlive | empty | — |
| 4 | GetSupportedFeatures | empty (the `0` argument is dropped by `_getRootCommand`, L61187) | `[count] { [featureId][version] } × count` |
| 5 | GetQLinkVersion | empty | `[minor lo][minor hi][middle][major]` → `"${d[3]}.${d[2]}.${d[1]<<8|d[0]}"` |
| 6 | GetActiveSessionInfo | empty | `[activeSID][timeout_s][clientType]` |
| 7 | RequestStateChange | `[state]` (1 = Active) | — |
| 8 | SendStateChangeDecision | `[otherSID][decision]` decision 0 Reject / 1 Accept / 2 Wait | — |

Client types (`$r`, L9668): 0 Unspecified, **1 QControl (desktop IO Center)**, **2 Web**. Session state (`rA`): 0 Inactive, 1 Active.

```js
// OpenSession payload, L58483
const A = Math.floor(Math.random() * 9e4) + 1e4, t = [...Pu(A)];   // u32 LE
this.clientType !== void 0 && t.push(this.clientType);               // Web = 2
// response, L58489
{ CONNECTION_ID: d[3]<<24|d[2]<<16|d[1]<<8|d[0], SESSION_ID: d[4], SESSION_STATE: d[5]||0, SESSION_TIMEOUT: d[6]||0 }
```

### 2.2 Keep-alive — HIGH
```js
// L16088
startKeepAlivePing(A) { ...; this._sessionTimer = setInterval(() => this.KeepAlive(), A * 1e3 / 2) }
```
Interval = **timeout / 2** → 1000 ms for the 2 s timeout. If ROOT feature version is 2, keep-alive runs even when the session is Inactive; on v1 only when Active (L16081).
During image upload the web app **stops** the keep-alive timer (`stopKeepAlivePing()` in `setMediaDockImage`, L16977) and restarts it afterwards → the stream of SetImage commands evidently keeps the session alive by itself (MED).

### 2.3 Notifications (ROOT, requestId 0) — HIGH
| Notif | Name | Data |
|---|---|---|
| 1 | SessionStateChanged | `[state]` (sent with target SID in SESSION byte) |
| 2 | ActiveSessionChanged | `[SID][timeout][clientType]` |
| 3 | StateChangeRequested | `[requesterSID][clientType][state][timeout]` |
| 4 | StateChangeDecision | `[SID][state][decision]` |

### 2.4 Multi-client arbitration — HIGH (web side), MED (device side)
* Device holds many sessions but only **one Active** session. Others are Inactive; mutating commands from an inactive session are expected to fail (status `NOT_ACTIVE`=9 exists; `setConfig` in the web app only sends when `SESSION_STATE === Active`, L61005).
* To take over: `RequestStateChange(1)`. The device notifies the current Active client with **StateChangeRequested**; that client answers with `SendStateChangeDecision(requesterSID, Accept|Reject|Wait)`; requester then receives **SessionStateChanged/StateChangeDecision**.
* The web app **auto-accepts** any request (L68465):
  ```js
  Q = H => d[F].GetActiveSessionInfo().then(() => d[F].SendStateChangeDecision(H.detail.data.SESSION_ID, Ld.Accept))
  ```
* In `Kn.readConfig` (L16838) the web app calls `GetActiveSessionInfo`; if the active client is **QControl** it does nothing (shows "session inactive" UI, user must click "continue" → `RequestStateChange(Active)`, L51028/L68441).
* Desktop log "SID 2; Active; Timeout 2s" = desktop IO Center (QControl) already owns the Active session. How the desktop answers StateChangeRequested is unknown (not in this bundle). **Your own app will need to either close IO Center or request Active and hope the desktop accepts.** Suggest identifying yourself as clientType 2 (Web) or 0; clientType 1 would impersonate the desktop app (unknown consequences).

### 2.5 Close — HIGH
`CloseSession([SID])`, then HID close. Web app closes sessions on `beforeunload`.

### 2.6 Observed startup sequence (Kn.readConfig L16838, web app)
`OpenSession(Web)` → `GetActiveSessionInfo` → `GetQLinkVersion` → `GetSerialNumber` → `GetSupportedFeatures(0)` → `GetDeviceInfo` →
(if not QControl-owned) `Numpad_GetState` → `MediaDock_GetState` → bindings/keyboard reads → `MediaDock_GetConfig` → `MediaDock_SetDateTime(localEpoch)` → polling rate / lighting reads.

---

## 3. Feature / command table (L10169–L10236) — HIGH

| FeatureId | Name | Commands |
|---|---|---|
| 1 | ROOT | see §2 |
| 2 | **DFU** | 1 Sync, 2 Transfer, 3 WriteData, 4 Validate, 5 Ready, 6 Commit, 7 Abort; notifs 1 ErrorOccurred, 2 DataRequest, 3 TransferComplete, 4 ValidationComplete, 5 ReadyComplete, 6 CommitComplete, 7 Aborted, 8 ResumePointChanged |
| 3 | DEVICE_INFO | 1 GetDeviceInfo, 2 GetSerialNumber, 3 SetSerialNumber, 4 ResetSerialNumber, **5 FactoryReset**; notif 1 DeviceInfoChanged |
| 4 | STORAGE | 1 GetStorageInfo, 2 WriteUserData `[off u16][len u16][data]`, 3 ReadUserData `[off u16][size u16]` |
| 5 | HUB | 1 GetState, 2 GetNodeInfo, 3 SendFrame; notifs 1..6 (FrameReceived=5) |
| 6 | USB_DEVICE | 1 GetPollingRate, 2 SetPollingRate |
| 7 | KEYBOARD | 1 GetLayout, 2 GetConfig, 3 SetConfig, 4 GetState, 5 SetState, 6 GetSnapTapConfig, 7 SetSnapTapConfig |
| 8 | MOUSE | 1 GetConfig, 2 SetConfig, 3 GetDPIConfig, 4 SetDPIConfig |
| 9 | BATTERY | 1 GetStatus |
| 10 | PAIRING | 1 GetState, 2 StartPairing, 3 StopPairing |
| 16 | LIGHTINGS | 1 GetLightingMode … 13 PerformRealtimeUpdates, 14 GetGlobalLayers, 15 GetCalibration, 16 SetCalibration |
| 17 | BINDINGS | 1 GetBindings, 2 SetBinding, 3 ClearBinding, 4 GetConfig, 5 SetConfig, 6 GetBinding |
| 18 | MACROS | 1..11 (…, 9 StartRecording, 10 StopRecording, 11 AbortPlayback) |
| **32** | **NUMPAD_MODULE** | **1 GetState, 2 SetImage, 3 GetImage**; notif 1 StateChanged, 2 ImageChanged |
| **33** | **MEDIA_DOCK_MODULE** | **1 GetState, 2 GetConfig, 3 SetConfig, 4 GetDateTime, 5 SetDateTime, 6 GetImage, 7 SetImage, 8 GetProfileInfo, 9 SetProfileInfo, 10 GetPlaybackInfo, 11 SetPlaybackInfo**; notif 1 StateChanged, 2 ImageChanged |
| 48 | SENSORS | 1..5 |
| 49 | CONTROLS | 1..10 |
| 50 | ARGB_DEVICE | 1..10 |
| 51 | DISPLAY_UNIT (LCD products, not keyboard) | 1 GetInfo, 2 GetTargetConfig, 3 SetTargetConfig, 4 GetMediaData, 5 SetMediaData |
| 52 | KEY_VALUE_STORAGE | 1 GetEntries … 5 SetValue, 6 DeleteEntry |
| 53 | ONE_CORD_DEVICE | — |
| 64 | STANDALONE_DISPLAY | 1 Get/2 SetBackgroundConfig, 3 Get/4 SetOverlayConfig |

Feature **versions** are only known at runtime from `GetSupportedFeatures` (`[count]{id,ver}`); the web app checks only `hasFeatureVersion(ROOT, 1|2)`.

Media-dock commands implemented by the web app (`_getMediaDockCommand`, L61344): GetState, GetConfig, SetConfig, GetDateTime, SetDateTime, GetImage, SetImage.
**Not implemented in the web app: Get/SetProfileInfo (8/9), Get/SetPlaybackInfo (10/11)** → these are used only by the desktop IO Center (QControl).

### 3.1 Media dock payloads — HIGH
* **GetState** (1): response `buffer[7]=state (0 Disconnected/1 Connected)`, `buffer[8]=position (0 None/1 Left/2 Right)`.
* **GetConfig** (2) response / **SetConfig** (3) request (9 bytes, L60732):
  `[menuR][menuG][menuB][clockMode 0=12h/1=24h][screensaverMode 0=Disabled/1=Clock/2=Image][ssTimeout u16 LE, seconds][turnOffTimeout u16 LE, seconds, 0=never]`.
  UI allows 1..240 s for both (L69995, input measure "s"). Defaults: color dc4d00, mode Image, ss 30 s, off 60 s.
* **Get/SetDateTime** (4/5): `u32 LE` seconds = local wall-clock epoch (`floor(Date.now()/1000 - tzOffsetMin/60*3600)`), sent on every connect.
* **GetImage** (6): `[type u8][offset u32 LE][size u32 LE]` → raw bytes.
* **SetImage** (7): `[type u8][offset u32 LE][bytes…]`.
  type = `w2`: **0 = Screensaver, 1 = Artwork** (L10233).

### 3.2 Numpad (Display Keys) payloads — HIGH
* **GetState** (1): `buffer[7]=state`, `buffer[8]=position`.
* **GetImage** (3): `[keyId u16 LE][offset u32][size u32]`.
* **SetImage** (2): `[keyId u16 LE][offset u32][bytes…]`.
  keyIds: `KEY_ID_NUMPAD_B1..B8 = 109..116`.

---

## 4. Image upload

### 4.1 Common image blob format — HIGH
Both modules store a blob addressed by byte offset:

```
offset 0: u32 LE  totalSize  (= payloadLen + 9, header included)
offset 4: u16 LE  width
offset 6: u16 LE  height
offset 8: u8      format   (Q7: 0 None, 1 RGB565, 2 PNG, 3 JPEG, 4 WebP)
offset 9: payload bytes
```
Readback helper confirms layout (L14090):
```js
hI = async (read, hdr=9, chunk=54) => { const i = await read(0, 9), r = new DataView(i.buffer);
  size = r.getUint32(0,true); width = r.getUint16(4,true); height = r.getUint16(6,true); format = r.getUint8(8);
  data = for (a = 9; a < size; a += 54) read(a, min(54, size-a)) ... }
```

### 4.2 Media dock screensaver upload — HIGH
```js
// Kn.setMediaDockImage, L16977
this.stopKeepAlivePing();
const i = await mn.base64ToImageData(A, 320, 240),       // canvas drawImage scaled to 320x240 (no aspect keep, no dithering)
      r = i.rgb16(),                                      // RGB565, 153600 bytes
      a = new Uint8Array([...Vs(r.length + 9, 4), ...Vs(i.width, 2), ...Vs(i.height, 2), Q7.RGB565]);
await this.sendMediaDockChunks(t /*w2.Screensaver=0*/, a, r, 49, 9);
...
async sendMediaDockChunks(type, header, data, chunk=49, hdrLen=9) {
  await this.MediaDock_SetImage(type, 0, header);
  for (let c = 0; c < data.length; c += chunk) await this.MediaDock_SetImage(type, c + hdrLen, data.slice(c, c + chunk));
}
```
**Exact wire sequence** (session already open & Active, FEATURE 0x21, CMD 0x07):
1. `SetImage [00][00 00 00 00][09 58 02 00][40 01][F0 00][01]` (header: 153609 bytes, 320×240, RGB565). Example full packet:
   `14 00 02 00 04 21 07 00 00 00 00 00 09 58 02 00 40 01 f0 00 01 00 … 00 66 c6` (SID 2, req 4).
2. For c = 0, 49, 98, … 153551: `SetImage [00][u32 LE (9 + c)][49 bytes of RGB565]` (last chunk 25 bytes). 3,135 data chunks → **3,136 request/response round-trips**.
   Each waits for the response (status must be 0) before the next.
3. Optionally `SetConfig` with screensaverMode = 2 (Image) — the image shows as **idle screensaver** after `screensaverTimeout` seconds.

**Pixel format** — HIGH:
```js
rgb16() { const A = new Uint16Array(this.data.length / 4);
  for (t...) A[t/4] = (R>>3)<<11 | (G>>2)<<5 | (B>>3);
  return new Uint8Array(A.buffer); }       // host (x86) byte order = little-endian
```
RGB565, **little-endian** (low byte first), row-major, top-left origin, 320 wide × 240 high, no rotation, plain truncation (no dithering, no gamma). Readback (`parseRGB16`) uses the same interpretation.

### 4.3 Display-key (numpad) upload — HIGH
```js
// Kn.setNumpadImage, L17018
const i = await mn.base64ToImageData(await mn.base64Normalize(t, 120, 120), 120, 120),   // re-encode JPEG q0.9 @120x120
      r = i.resize(120, 120).rotate(90).jpeg(),                                          // rotate +90°, JPEG quality 1.0
      a = new Uint8Array([...Vs(r.length + 9, 4), ...Vs(i.width, 2), ...Vs(i.height, 2), Q7.JPEG]);
await this.sendNumpadChunks(A /*keyId 109..116*/, a, r, 49, 9);
```
* Format **JPEG (3)**, **120×120** (not 140×140 as assumed), content **rotated 90° clockwise** before encoding (readback rotates −90).
* Per packet: `[keyId u16][offset u32][49 bytes]` = 55 bytes = exactly the first-frame max.
* A full-quality 120×120 JPEG is typically 5–15 KB → ~100–300 round trips per key.

### 4.4 Persistence (flash vs RAM) — MED/LOW
* No explicit "commit/save/erase/flash" command exists for images in the web app; the blob becomes effective after the last byte (offset + len == totalSize) (MED).
* The web app **reads the current screensaver/key images back from the device on every page load** (`getMediaDockImage` L70205, `getNumpadImages` L45476) and profile import re-uploads them — i.e. images persist across host sessions and are the device-side source of truth → almost certainly stored in **non-volatile flash** (MED). Not verified across power cycles.
* **Implication:** streaming 150 KB frames into slot 0 repeatedly may wear the flash. Test first: upload, unplug, replug; if the image survives, treat slot 0 as flash and do **not** stream into it continuously.
* Type **1 = Artwork** is the album-art slot used by the desktop app for "now playing"; it is a much better candidate for volatile/live pushes (LOW — format assumed identical, unverified).

### 4.5 zlib/deflate — HIGH
The pako/deflate code is part of bundled **JSZip 3.10.1** (L65157) used only for profile export/import `.zip` files (`media_dock*` entries, L68109). **Not used on the wire.** Images are raw RGB565 (dock) or JPEG (keys).

### 4.6 Web-app image preprocessing (class `mn`, L16412) — HIGH
* `base64ToImageData(src, w, h)`: `ctx.drawImage(img, 0, 0, w, h)` → stretch to target size (bilinear by browser), `getImageData`.
* `resize`, `rotate(deg)` via canvas transforms; `jpeg(q=1)`, `png()`, `webP(q=1)` via `canvas.toDataURL`.
* `rgbaToRgb565` (rounding variant) exists but upload uses `rgb16()` (truncating). No dithering anywhere.

### 4.7 Throughput estimate — MED
* Per chunk: 1 OUT (64 B) + 1 IN (64 B), strictly sequential; 49 useful bytes per round trip.
* Full frame: 3,136 round trips. At 1 ms HID interval (full-speed, bInterval 1): ≈2 ms/round trip ⇒ **~6 s/frame (~0.16 fps)**; optimistic 1 ms ⇒ ~3 s (~0.3 fps). If the vendor interface is high-speed with 125 µs interval: ~0.4–0.8 s (~1–2 fps). Firmware (and possibly flash-write) latency adds on top.
* Speedups to try (LOW, untested by web app on this device):
  1. **Multi-frame messages**: the generic transport supports payloads > 55 B split into continuation frames (seq bits, 59 B each) with **one** response; e.g. 4 KB per SetImage ⇒ ~70 OUT reports per ACK ⇒ up to ~59 KB/s at 1 ms ⇒ ~2.6 s/frame. Firmware support for SetImage reassembly is unknown; beware the web app's continuation LENGTH quirk (§1.1).
  2. **Partial updates**: SetImage is offset-addressed, so writing only changed rows (graph region) may work if the firmware re-renders on partial writes — unknown.
  3. Use JPEG/PNG format byte (formats 2/3/4 exist in the enum) to shrink payload — unknown whether the media-dock decoder accepts non-RGB565 (numpad accepts JPEG).

---

## 5. "Now playing" / media info path — MED
* MEDIA_DOCK_MODULE has **GetProfileInfo(8)/SetProfileInfo(9)** and **GetPlaybackInfo(10)/SetPlaybackInfo(11)**, plus image type **Artwork (1)**. These are not implemented in the web bundle, so their payload layout is not recoverable here. The desktop IO Center (QControl) must be the one pushing track title/artist/progress (SetPlaybackInfo), active profile name (SetProfileInfo) and album art (SetImage type 1).
* This proves a host-driven live-update path exists. To get the payload format: capture desktop IO Center traffic with USBPcap/Wireshark while a track plays, or reverse the desktop binary (look for the same enums).

---

## 6. Dangerous commands — AVOID

| Feature/Cmd | Why |
|---|---|
| **DFU (2) / all** — Sync(1) `[upgradeId u32][count][mcu…]`, Transfer(2) `[mcu][crc16][size u32]`, WriteData(3) `[mcu][data]`, Validate(4), Ready(5), **Commit(6)**, Abort(7) | Firmware update; can brick / leave device in bootloader (PID 0x0009) |
| DEVICE_INFO **FactoryReset (3/5)** (payload = serial string) | Wipes all settings, bindings, images |
| DEVICE_INFO **SetSerialNumber (3/3)**, **ResetSerialNumber (3/4)** | Factory provisioning |
| STORAGE **WriteUserData (4/2)** | Writes raw user flash area |
| KEY_VALUE_STORAGE SetValue/DeleteEntry (52/5,6) | Persistent storage |
| HUB **SendFrame (5/3)** | Tunnels raw frames to other nodes |
| LIGHTINGS SetCalibration (16/16) | Factory LED calibration |
| USB_DEVICE SetPollingRate (6/2) | May re-enumerate the device |
| Raw non-QLink write `writeReport(0, [0xFF, 0xFE, 0x00, layout])` (`_setKeyboardLayout`, L16003) | Changes keyboard layout outside QLink framing |
| Anything when PID = **0x0009** (bootloader) | Only ROOT+DFU available |
| Impersonating clientType 1 (QControl) | Unknown arbitration side effects |

Safe set for your app: ROOT 1/2/3/4/5/6/7(/8), DEVICE_INFO 1/2, MEDIA_DOCK 1/2/3/4/5/6/7, NUMPAD 1/2/3.

---

## 7. Minimal client recipe (derived)
1. Open HID interface VID 0x373F PID 0x0001, usage page 0xFF00; 64-byte reports, report ID 0.
2. `OpenSession` (SID byte 0): `[rand u32 LE][2]` → read SID, state, timeout.
3. If state = Inactive: `GetActiveSessionInfo`; if QControl owns it, `RequestStateChange([1])` and wait for SessionStateChanged(Active) notification (or close IO Center).
4. Start KeepAlive every `timeout/2` s (1 s) — pause while streaming chunks (any command appears to count).
5. `MediaDock_GetState` → must be Connected. `GetSupportedFeatures` (empty payload) to see MEDIA_DOCK version.
6. Upload per §4.2. Handle notifications (requestId 0) out-of-band; match responses by feature+command; 1 s timeout per command.
7. `CloseSession([SID])` on exit.
