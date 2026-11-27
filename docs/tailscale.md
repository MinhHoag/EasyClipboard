# Tailscale workflow

Use the existing native Send to Laptop Shortcut across networks, targeting the laptop's stable Tailscale IPv4 address. No custom iPhone certificate profile is needed for this mode: HTTP runs inside Tailscale's encrypted device-to-device connection. HTTP itself is not providing encryption.

## Connect once

1. Connect the laptop and iPhone to the same Tailscale network, or configure access between them through your tailnet's policy. If using Shadowrocket's integrated Tailscale feature, use that configured integration on the phone. Running separate VPN apps can disconnect each other on iOS; do not assume the standalone Tailscale app can coexist with Shadowrocket.
2. Open EasyClipboard from the Start menu, or use **Start EasyClipboard.cmd** for a source build.
3. Select the laptop's `100.x.x.x` address and click **Start receiving**. If the list is empty, connect Tailscale and select **Refresh networks**.
4. On the iPhone, open the laptop's displayed address in Safari, with just `/` after port 47831, for example `http://100.70.0.1:47831/`. It should say the Tailscale receiver is reachable. Replace the example address with your laptop's actual address.
5. In **both** Shortcut POST actions, replace only the URL with `http://<laptop-Tailscale-IP>:47831/v1/transfers`. Keep the Authorization header, pairing key, transfer ID, text/image JSON, and response checks unchanged.

The Shortcut targets the laptop. The phone's own IP does not go in that URL. Pairing credentials are still required even on the tailnet. If you previously shared a test key, rotate it after connection troubleshooting and update both headers.

## Scoped Windows firewall rule

If Safari cannot connect, stop receiving, unlock developer mode from the settings gear and start receiving again in Tailscale mode. The setup panel then shows the **Firewall interface ID**. In an Administrator PowerShell opened to the project folder:

```powershell
.\scripts\firewall.ps1 -Action Enable -NetworkMode Tailscale -InterfaceId 'THE-TAILSCALE-INTERFACE-GUID-FROM-THE-APP'
```

This rule allows TCP 47831 only for the receiver executable, the Tailscale interface, its local address, and remote addresses in `100.64.0.0/10`. It uses all Windows network profiles because the virtual interface may be classified Public, while the other filters keep it off ordinary Wi-Fi/Ethernet interfaces. The pairing key and Tailscale access controls still apply. It does not open router ports or publish the receiver to the internet.

To remove it:

```powershell
.\scripts\firewall.ps1 -Action Disable -InterfaceId 'THE-SAME-TAILSCALE-INTERFACE-GUID'
```

The helper is supplied and syntax-checked; no live firewall rule was changed during implementation. Existing home-network rules are separate. The default source launchers match the executable path used by this helper; a different publish location or launching the DLL via dotnet requires a correspondingly scoped executable path.

## Daily use and limits

Keep Tailscale connected, the laptop awake, and EasyClipboard receiving. Copy one item on the phone, tap Send to Laptop, wait for confirmation, then Ctrl+V. The phone can be on cellular data or another Wi-Fi network while the laptop has its own network connection. Do not replace the stable Tailscale URL with the laptop's changing house/school IP.

If a failure temporarily clears after Stop / Start receiving, check the **Phone requests** line at the bottom of the receiver while retrying the Safari page or Shortcut. It resets on Start and shows only request counts, time, page/transfer classification, active requests, and HTTP outcome. It never displays or saves clipboard content, credentials, or full URLs. A local check from the PC does not increment it. If the count stays unchanged, no new phone HTTP request reached the application's request handler; network routing, firewall, and failures before HTTP parsing remain possible. An increasing count with an HTTP error narrows the failure to the receiver response. An active transfer that does not finish is a different clue from a request that never arrives. These diagnostics do not prove that the phone received a response.

Start and Stop are serialized, and transition controls are disabled until completion. Restarting keeps the pairing credentials and opens in Tailscale mode.

Tailscale requires an account and coordination service, and may relay encrypted traffic. This is an explicitly chosen internet-capable option, different from the original local-only/no-account scope. Public networks may require portal login or block VPN traffic; performance and reachability are not universal. Truly offline operation with no usable link between devices is not guaranteed. The original home LAN modes remain available separately.

For Moonlight, the same laptop address can identify a configured Sunshine/compatible streaming host. EasyClipboard neither installs nor configures that host, Moonlight, or a Switch client.

## Validation

71 local checks pass, including Tailscale IPv4 range boundaries and peer acceptance despite the virtual adapter's /32 mask. An additional short-lived test successfully bound a receiver to this laptop's actual Tailscale interface and authenticated a local request with disposable test credentials. It used a simulated clipboard and did not change the live receiver or its history.

Five additional stop/start cycles reuse the same port and pooled HTTP clients, checking reconnection, authenticated transfers, rejected credentials, saved receipts, and peer request diagnostics. The checks also verify that local health requests do not appear as phone activity and that a diagnostic observer failure cannot interrupt a transfer. These are local receiver checks, not a reproduction or resolution of the reported Shadowrocket/cellular failure.

Five further image regression checks preserve every pixel at 72, 96, 144, 192, and 300 DPI. They reproduce and guard against the former DPI-dependent shrink-inside-canvas bug, for 81 passing local checks/cycles in total.

This confirms local binding/routing policy, not a real iPhone tunnel transfer. The phone-to-laptop Tailscale and cellular/public-Wi-Fi tests remain to be performed. The existing home HTTP text/photo flow was reported working by the user.

Sources: [stable Tailscale addresses](https://tailscale.com/docs/concepts/ip-and-dns-addresses), [Tailscale encryption](https://tailscale.com/docs/concepts/tailscale-encryption), [VPN coexistence limitations](https://tailscale.com/docs/reference/faq/other-vpns).
