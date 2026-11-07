# Test the private connection

This is the earlier diagnostic-only tool. For the text/photo receiver prototype, use [setup.md](setup.md) and [shortcut.md](shortcut.md). The checks below remain useful but no longer gate implementation.

This checkpoint tests a private network between laptop and iPhone. It does not transfer clipboard items. No custom iPhone app is needed.

## Start

1. Use Windows 11 22H2 or later with a Wi-Fi Direct capable adapter/driver. Keep the Wi-Fi radio on. Turn off Windows **Mobile Hotspot** and stop wireless display sessions; those can conflict with the private AP API.
2. Open PowerShell in the project folder and run `./scripts/run-hotspot-test.ps1`. The first build needs internet for Microsoft's targeting package; running an already-built prototype does not. Use `-NoBuild` to launch the existing Release build.
3. Click **Start private hotspot**. This creates an autonomous Wi-Fi Direct Group Owner in legacy AP mode. It does not use the deprecated `netsh wlan start hostednetwork` feature and does not enable Internet Connection Sharing.
4. Wait for the local URL. The window displays a generated network name, a **Show password** checkbox, the AP interface ID, and a random probe code. Credentials stay in process memory and the local window; do not paste them into bug reports.
5. On iPhone, open **Settings → Wi-Fi**, select that exact network, and enter its password. If iOS reports no internet, stay on this Wi-Fi for the test. A Wi-Fi privacy/MAC-address setting should not need changing.
6. In Safari on iPhone, type the displayed `http://<AP-address>:47831/v1/probe` URL. A JSON response with the same `probeId` confirms this session's endpoint is reachable. Windows also reports a request from another device. Neither observation alone identifies that device as your iPhone; verify the response on your phone.

This HTTP endpoint carries only public connectivity information. It never reads a clipboard, accepts uploads, or sends pairing keys. It is not the encrypted clipboard receiver. Do not send private data or use the school Wi-Fi IP. Do not expose the port through a router.

## Windows firewall

If Windows shows its general network-access prompt, cancel it and use the narrow rule below instead of granting blanket public/school-network access. The probe does not change the firewall automatically.

With the hotspot running, copy the **Interface ID** from its window. In an **Administrator PowerShell**, open this project folder and run:

```powershell
.\scripts\probe-firewall.ps1 -Action Enable -InterfaceId 'THE-INTERFACE-GUID-FROM-THE-WINDOW'
```

Replace the placeholder with the actual GUID. The script verifies an active Wi-Fi Direct adapter with one private IPv4 address. It permits inbound TCP 47831 only for the selected dotnet executable, that adapter, its local address, and its subnet. It does not disable the firewall. `Profile Any` accommodates the AP being classified as Public; interface/address restrictions keep the rule off the school interface.

Retry Safari. If organization policy overrides local firewall rules, do not weaken that policy; use the result to decide whether this project is suitable there.

After testing, remove the rule using the same GUID, even if the hotspot is already stopped:

```powershell
.\scripts\probe-firewall.ps1 -Action Disable -InterfaceId 'THE-INTERFACE-GUID-FROM-THE-WINDOW'
```

Removal targets only the named EasyClipboard probe rule. A changed AP address requires recreating the rule; no other rules are modified.

## Stop and report

Click **Stop** or close the window. The app releases its publisher, peer connections, and HTTP listener. The password remains stable during that process lifetime and changes on the next launch. Stopping does not erase the saved iPhone Wi-Fi entry; use **Forget This Network** if desired.

Report these observations without passwords, unrelated network names, or personal clipboard content:

- Did the private network appear in iPhone Wi-Fi settings?
- Did Safari show the matching probe code?
- Did it work without upstream internet?
- If the laptop was also on permitted shared Wi-Fi, did that connection remain usable?
- What happened to iPhone internet access while joined, and was switching Wi-Fi acceptable?

Do not disconnect an active network merely to test offline behavior if it would interrupt work. The app does not change saved school/home profiles or choose which network supplies internet. Concurrent Wi-Fi behavior is driver-dependent and remains untested.

## Troubleshooting

- **ResourceInUse / could not start:** stop Windows Mobile Hotspot or wireless projection, ensure Wi-Fi is on, then retry. Do not change registry settings or disable security software.
- **Access denied:** Windows policy or API access may block the operation. Record the displayed error code. Elevation is needed for the firewall script, not assumed to fix the AP API.
- **No address / multiple addresses:** the probe refuses to bind a wildcard or guess an address. Close other Wi-Fi Direct sessions and retry. Consider the manufacturer's current driver if permitted.
- **Wi-Fi joins but Safari times out:** verify the exact AP URL and port, inspect the narrow firewall rule, and check the app is running. A laptop self-check does not prove phone connectivity.
- **Mobile Hotspot starts or radio disappears:** Windows can stop the private AP. The app closes its HTTP service; stop/restart in the window after resolving the conflict.
- **No internet on iPhone:** expected for this local-only access point. Whether cellular internet remains usable is an observation to test, not a guarantee.

## Implemented versus unverified

The Release build and 19 local checks pass. The laptop advertises Wi-Fi Direct GO capability. Actual AP startup was rejected by approval review and was not run; the tool returned no reason. Neither an iPhone Wi-Fi join nor a Shortcut transfer has been tested. The UI and firewall changes have not been exercised live.

The separate receiver prototype now implements HTTPS onboarding, revocable pairing, text/photo transfer, local history, Windows clipboard handling, and the tray window. It does not reuse this anonymous HTTP endpoint for private transfers. Actual phone and cross-app tests remain outstanding.
