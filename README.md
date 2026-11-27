# EasyClipboard

A Windows clipboard receiver for copied iPhone text and images. Send from a native iPhone Shortcut, then paste on Windows with **Ctrl+V**. The default connection uses **Tailscale** and needs no iPhone certificate profile.

**PC to iPhone is also available:** use the native **Get from PC** Shortcut. Fetching turns on when the receiver starts and reads only on an authenticated request. Follow [the reverse-transfer setup](docs/get-from-pc.md).

Follow the [Tailscale setup guide](docs/tailscale.md). LAN and hotspot modes are available under **Settings → Unlock developer mode**. Choose a private local developer password the first time you unlock them.

## Run

Double-click **Start EasyClipboard.cmd**, or run this from PowerShell in the project folder:

```powershell
.\scripts\run.ps1
```

The current workspace includes a local .NET 10 SDK. Other machines need the **.NET 10 SDK** for the source build. First restore needs internet; normal transfers do not.

1. Connect the PC and iPhone to the same Tailscale network.
2. Select the laptop's Tailscale address, click **Start receiving**, and open **Pairing / setup**.
3. Follow [the native Shortcut setup](docs/shortcut.md) with the displayed URL and your own pairing key.
4. Copy text or a photo on iPhone, run **Send to Laptop**, and paste on Windows.

If the iPhone cannot reach the receiver, follow the [Tailscale troubleshooting and scoped firewall instructions](docs/tailscale.md).

## Package a Windows release

Run `pwsh -File scripts/package.ps1 -Version 0.2.1` to build an x64 installer and portable ZIP with .NET included. The installer and update files are written under `artifacts/<version>/release`.

To enable updates, pass `-UpdateFeed https://updates.example.com/easyclipboard/` with a trusted HTTPS folder you control. Publish the generated update manifest and full package to that folder for each release. An installed app checks on launch and asks before downloading and restarting. Builds without a configured feed can be upgraded by running a newer installer. Pairing and history stay under `%LOCALAPPDATA%\EasyClipboard`, separate from the app installation. Release files built without a signing certificate are unsigned.

## What the prototype includes

- Text with Unicode, emoji, and multiple lines; real PNG image data on the Windows clipboard.
- One revocable 256-bit pairing key. **Home HTTP is deliberately unencrypted**, including the key in transit, as selected for this prototype. Optional home HTTPS and private-hotspot HTTPS modes remain available with certificate setup.
- A tray menu, pause receiving, history previews, Copy Again, deletion, clear history, and storage limits.
- Local history under `%LOCALAPPDATA%\EasyClipboard`, encrypted with Windows DPAPI for the current user. Defaults: 30 days, 200 entries, 256 MB. Change these in **Storage limits**.
- Versioned requests, size/decoding checks, safe atomic files, and persistent retry receipts while entries are retained.
- Optional basic HTML formatting with plain-text fallback; the supplied Shortcut recipe sends reliable plain text.

Closing the window keeps the app in the tray. Use **Quit** in the tray to stop it. Nothing is typed or pasted automatically. No clipboard monitoring runs on iPhone. Win+V behavior depends on Windows settings and item compatibility; ordinary Ctrl+V is the target.

## Validation and limits

The build passes without warnings. **103 local checks and restart cycles pass**, including actual loopback HTTP/HTTPS requests, Tailscale routing policy, authentication diagnostics, malformed input, text/image persistence, retries, interrupted uploads, retention, and five same-port receiver restarts. Pixel-by-pixel PNG checks at 72, 96, 144, 192, and 300 DPI guard against resizing image content inside an unchanged canvas. 22 reverse-transfer checks cover permission, fresh content, raw text/PNG responses, cancellation, and revocation without changing the PC clipboard or history. A separate local binding/authentication check passed on the laptop's actual Tailscale interface. The Windows clipboard is simulated in these checks.

Native Shortcut text and photo transfers have been tested on home Wi-Fi. Intermittent image-upload timeouts remain under investigation. The current pipeline converts images to PNG and Base64 before HTTP upload. Cellular connectivity through Shadowrocket, private hotspot startup, and live clipboard contention require separate device testing.

Home HTTP receives authenticated transfers on port 47831 without a certificate profile. It is intended for this house-network prototype, with the accepted privacy tradeoff. Optional HTTPS receives on 47832 and uses 47831 only for public certificate onboarding; HTTP transfers are rejected in those modes. No TLS verification is disabled.

Run checks with `./scripts/check.ps1`. Architecture, protocol, and build notes are in [docs/architecture.md](docs/architecture.md). An earlier diagnostic-only prototype remains available through `./scripts/run-hotspot-test.ps1`; do not run both apps at once.

No real pairing keys, certificates, or clipboard contents belong in the repository. Test data and tools stay in ignored `.tools/`. No remote repository has been created. **License selection remains deferred.** See [dependency notes](docs/dependencies.md) before distributing binaries.
