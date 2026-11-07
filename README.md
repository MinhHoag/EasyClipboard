# EasyClipboard

A Windows prototype for sending one copied iPhone text item or photo over your **house Wi-Fi**, then pasting on your laptop with **Ctrl+V**. The iPhone uses a native **Send to Laptop** Shortcut. The default home HTTP mode needs no ESP32, iPhone app, or certificate profile.

**Tailscale mode is now available:** use the laptop's stable Tailscale address across networks, with HTTP carried inside its encrypted tunnel and no custom certificate profile. Follow [docs/tailscale.md](docs/tailscale.md). Existing home LAN modes remain available for local transfers.

## Run

Double-click **Start EasyClipboard.cmd**, or run this from PowerShell in the project folder:

```powershell
.\scripts\run.ps1
```

The current workspace includes a local .NET 10 SDK. Other machines need the **.NET 10 SDK** for the source build. First restore needs internet; normal transfers do not.

1. Keep both devices on your house network. Leave **Home Wi-Fi / LAN — HTTP** selected.
2. Select the laptop's active Wi-Fi/Ethernet address, click **Start receiving**, and open **Pairing / setup**.
3. Follow [the native Shortcut setup](docs/shortcut.md), using the displayed HTTP URL and pairing key. Skip all certificate/profile steps in this mode.
4. Copy text or a photo on iPhone, run **Send to Laptop**, and paste on Windows.

If the iPhone cannot reach the receiver, use the [scoped firewall instructions](docs/setup.md). No router port forwarding is needed. The laptop can use Ethernet if it reaches the same home LAN as the phone's Wi-Fi. Guest-network isolation can still block the connection.

## What the prototype includes

- Text with Unicode, emoji, and multiple lines; real PNG image data on the Windows clipboard.
- One revocable 256-bit pairing key. **Home HTTP is deliberately unencrypted**, including the key in transit, as selected for this prototype. Optional home HTTPS and private-hotspot HTTPS modes remain available with certificate setup.
- A tray menu, pause receiving, history previews, Copy Again, deletion, clear history, and storage limits.
- Local history under `%LOCALAPPDATA%\EasyClipboard`, encrypted with Windows DPAPI for the current user. Defaults: 30 days, 200 entries, 256 MB. Change these in **Storage limits**.
- Versioned requests, size/decoding checks, safe atomic files, and persistent retry receipts while entries are retained.
- Optional basic HTML formatting with plain-text fallback; the supplied Shortcut recipe sends reliable plain text.

Closing the window keeps the app in the tray. Use **Quit** in the tray to stop it. Nothing is typed or pasted automatically. No clipboard monitoring runs on iPhone. Win+V behavior depends on Windows settings and item compatibility; ordinary Ctrl+V is the target.

## Validation and limits

The build passes without warnings. **81 local checks and restart cycles pass**, including actual loopback HTTP/HTTPS requests, Tailscale routing policy, authentication diagnostics, malformed input, text/image persistence, retries, interrupted uploads, retention, and five same-port receiver restarts. Pixel-by-pixel PNG checks at 72, 96, 144, 192, and 300 DPI guard against resizing image content inside an unchanged canvas. A separate local binding/authentication check passed on the laptop's actual Tailscale interface. The Windows clipboard is simulated in these checks.

The user reports the home HTTP native Shortcut text/photo flow working. **Not yet verified:** a real iPhone Tailscale transfer, actual hotspot startup, or live clipboard contention/recovery. The adapter reports Wi-Fi Direct GO support, but that is not proof of hotspot operation. This is a source-build prototype, not a tested release.

Home HTTP receives authenticated transfers on port 47831 without a certificate profile. It is intended for this house-network prototype, with the accepted privacy tradeoff. Optional HTTPS receives on 47832 and uses 47831 only for public certificate onboarding; HTTP transfers are rejected in those modes. No TLS verification is disabled.

Run checks with `./scripts/check.ps1`. Architecture, protocol, and build notes are in [docs/architecture.md](docs/architecture.md). An earlier diagnostic-only prototype remains available through `./scripts/run-hotspot-test.ps1`; do not run both apps at once.

No real pairing keys, certificates, or clipboard contents belong in the repository. Test data and tools stay in ignored `.tools/`. No remote repository has been created. **License selection remains deferred.** See [dependency notes](docs/dependencies.md) before distributing binaries.
