# Prototype setup and troubleshooting

For Tailscale, use [tailscale.md](tailscale.md). That mode selects only the Tailscale virtual interface, accepts tailnet peers in `100.64.0.0/10` rather than comparing them to a /32 adapter subnet, and uses HTTP inside the encrypted tunnel. The new firewall option is `-NetworkMode Tailscale` (HTTP transport). Home/private-hotspot instructions follow below.

Run **Start EasyClipboard.cmd** or `./scripts/run.ps1`. The app defaults to **Home Wi-Fi / LAN — HTTP**, with receiving off. First-time build needs the .NET 10 SDK and access to NuGet. The existing workspace has a project-local SDK; transfers need no internet.

Keep both devices on the house network. Select the laptop's active Wi-Fi/Ethernet address, click **Start receiving**, then **Pairing / setup**. Follow [shortcut.md](shortcut.md) using the displayed HTTP URL and key. No iPhone profile, ESP32, SSH server, or private hotspot is needed for this selected mode. The key remains required, but HTTP content and the key are unencrypted in transit, as accepted for this prototype.

If no addresses are listed, connect to the home network and click **Refresh networks**. Multiple entries can include virtual/VPN adapters; choose the physical Wi-Fi/Ethernet address your phone can reach. The receiver binds only that address and stops accepting requests if it disappears. Update the Shortcut and firewall rule if DHCP changes it; a router DHCP reservation can keep it stable. No router port forwarding is needed.

## Firewall

The app does not change firewall rules itself. Cancel Windows' broad network-access prompt and use this narrower rule if the phone cannot connect. With receiving active, copy its **Firewall interface ID** from the main window. From an **Administrator PowerShell** in the project folder:

```powershell
.\scripts\firewall.ps1 -Action Enable -InterfaceId 'THE-INTERFACE-GUID-FROM-THE-APP'
```

By default, the script validates the selected home adapter/private address and requires Windows to classify that network as **Private**. It permits TCP 47831 only for **EasyClipboard.exe**, that interface, local address, and subnet, on the Private profile. It leaves the firewall enabled. If the house network is classified Public, change its network profile to Private in Windows Settings only if it is your trusted home connection. Do not do this to school/library networks.

The provided launchers use the exact executable matched by the rule. A manual DLL launch through dotnet.exe will not match that different process. To use optional home HTTPS, add `-Transport Https` (opens 47831 and 47832). For optional private-hotspot HTTPS, add `-NetworkMode Hotspot -Transport Https`; only that mode accepts a Wi-Fi Direct interface and uses `Profile Any`, still scoped to its interface/address/subnet/program.

Remove the rule with the same interface GUID when finished:

```powershell
.\scripts\firewall.ps1 -Action Disable -InterfaceId 'THE-INTERFACE-GUID-FROM-THE-APP'
```

Recreate it if the address changes or the project moves. Do not disable the firewall or override organization policies. The script is provided and syntax-checked; it has not been run against the live firewall.

## Common issues

| Symptom | Next check |
| --- | --- |
| Hotspot won't start | Keep Wi-Fi on, Windows Mobile Hotspot/wireless projection off. Record the error code. Wi-Fi Direct GO must be supported by the adapter/driver. |
| Multiple/no AP addresses | Stop conflicting Wi-Fi Direct sessions. The app refuses to guess or listen on every interface. |
| Phone cannot connect at home | Use the displayed URL/address, check the scoped firewall rule, and keep the app/laptop awake. Put both devices on the main LAN; guest Wi-Fi/client isolation may block communication. |
| TLS fails in optional HTTPS mode | Verify the installed root, enable full trust, check clocks, and use the current local IP. Home HTTP needs no certificate/profile. |
| Cannot paste | Confirm Shortcut success and use an app supporting the content. For images, try Paint; for text, Notepad. Copy Again retries clipboard access. |
| Receiving continues after closing | Closing hides the window. Open or quit through the tray icon. |
| No phone internet while joined | This AP provides a local connection only. Cellular fallback and simultaneous school Wi-Fi depend on iPhone behavior/laptop drivers and remain untested. |

The inspected adapter reports Wi-Fi Direct GO support but no old Hosted Network/Soft AP support. The app therefore uses modern Wi-Fi Direct APIs. Actual AP startup and iPhone use remain untested; a previous startup command was rejected by approval review. The receiver prototype is available without making that test a prerequisite for implementation.

## Local data

Data stays under `%LOCALAPPDATA%\EasyClipboard`:

- `identity.dat`: DPAPI-encrypted root/private key, pairing key, and Wi-Fi credentials.
- `history/<UUID>.dat`: DPAPI-encrypted content and transfer receipt, written atomically.
- `preferences.json`: non-secret retention limits. `config.example.json` documents the shape; it is not auto-imported from the repository.

Default limits: 30 days, 200 items, 256 MB of encrypted history files. Limits run at startup, after successful transfers, and when changed in the UI. Pending writes may temporarily exceed limits. Corrupt/unreadable history raises a warning. This prototype has no timer to prune while completely idle.

DPAPI ties private files to this Windows user; copying them to another account/machine is not an export feature. Administrators or malware acting as your user are outside that protection. Deleting history does not erase content already on the Windows clipboard or in native Win+V/cloud history. Configure Windows clipboard sync separately if you require local-only behavior outside this app.

To reset: quit through the tray first, remove the Shortcut and scoped firewall rule, then back up or remove the app's local data folder manually. If you used optional HTTPS, remove its iPhone certificate profile too. A reset requires pairing again. Do not erase data while receiving is active.
