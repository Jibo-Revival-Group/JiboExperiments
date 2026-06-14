# WiFi QR Code - OOBE Flow

Jibo's out-of-box setup (OOBE) skill reads one or more QR codes from its
camera to obtain WiFi credentials and an access token. This doc covers the wire
format, the two generator tools, and how they interoperate with the OpenJibo
server.

## Wire Format

### Payload

Plaintext, before encoding:

```text
<ssid>
<password>
[<staticIP>]
[<netmask>]
[<gateway>]
[<dns1>]
[<dns2>]
<accessToken>
```

Fields are newline-separated. The static IP block is present only if a static
IP is configured; if absent, the robot defaults to DHCP. `accessToken` is
always the last line.

### XOR encoding

Each byte of the plaintext payload is XOR-ed against the repeating key:

```text
Wow, you cracked our secret code. Impressive. Maybe you should check out jibo.com/jobs.
```

The result is a binary-safe string of the same length.

### Chunking

The encoded string is split into chunks of `MAX_CHARS_PER_CODE = 25`
characters. Each chunk becomes one QR code:

```text
<id>/<total>
<encoded_chunk>
```

The first line is `<id>/<total>` (for example, `1/3`). The second line is the
raw encoded chunk. A typical setup payload produces 2-3 codes.

Jibo's OOBE skill reads the codes in order and reassembles the payload before
decoding. Codes can be rescanned if missed.

## Access Token

The OOBE skill requires a token to authenticate the setup with the cloud. With
OpenJibo:

- From server: call `OOBE_20161026.PrepareRobot` on the running .NET server.
  Returns a short-lived token scoped to the loop.
- Static fallback: if the server is unreachable, both tools can fall back to
  the static token `JiboLivesSo`. This works for setups where the robot connects
  to the network and then reaches the cloud.

## HTML Generator

Path: `src/Jibo WiFi QR Generator/jibo_qr_generator.html`

Standalone single-file HTML tool. No build step.

Features:

- SSID, password, optional static IP fields
- Optional server URL to fetch a live token from OpenJibo
- Chunk size selector
- Displays all QR codes side by side in a grid
- Per-code download buttons

## React Native App

Repo: `Jibo_APP/`

The `ScreenQR` screen displays codes one at a time with Prev/Next navigation
and a "Next code" / "Done" primary button. The screen activates keep-awake and
sets brightness to maximum while displaying codes, then restores both on
unmount.

Configuration:

```sh
EXPO_PUBLIC_OPENJIBO_SERVER_URL=http://192.168.1.X:24605
```

If the variable is absent or the server is unreachable at setup time, the app
falls back to the static token.

Key source files:

| File | Role |
|---|---|
| `src/wifiQr.ts` | Payload builder, XOR encoder, chunker, parser |
| `src/screens/ScreenQR.tsx` | QR display with one-at-a-time pager |
| `src/screens/ScreenSetup.tsx` | Post-scan OOBE status polling |

## Interoperability

Both tools produce identical output for the same input. The
`parseJiboSetupQrData()` function in `wifiQr.ts` is the canonical decoder and
can be used to verify any generated code.
