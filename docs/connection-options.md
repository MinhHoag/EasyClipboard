# Connection decision — house Wi-Fi

The current selected workflow is **house Wi-Fi, native Shortcuts, and paired HTTP**, with no iPhone profile or ESP32. The user explicitly accepted unencrypted transport to simplify the prototype. HTTP sends content and the pairing key unencrypted; this revises the original in-transit encryption requirement for this chosen mode. Optional home HTTPS and private-hotspot HTTPS remain available. The text/photo receiver, history, and Shortcut recipe are implemented, but real iPhone and cross-app paste tests remain outstanding. Start with [setup.md](setup.md).

## What can fail on shared Wi-Fi

A captive portal or username/password login is an authentication step. It does not by itself determine whether two clients can communicate. Client isolation, VLAN separation, access-control rules, and local Windows firewall rules can independently prevent the iPhone from reaching the laptop.

[Cisco documents client isolation](https://documentation.meraki.com/Wireless/Operate_and_Maintain/How_Tos/Firewall_and_Traffic_Shaping/Wireless_Client_Isolation): isolated clients cannot communicate with other devices on the same VLAN. Encryption and pairing cannot make an unreachable endpoint reachable.

The app must not depend on users changing school network controls. A private wireless connection may also be subject to local policy; do not present one as permission to circumvent those rules.

## Options

| Connection | Uses the shared network for clipboard traffic? | Main tradeoffs | Current evidence |
| --- | --- | --- | --- |
| Existing house Wi-Fi — selected | Yes | Native Shortcut with paired HTTP, no profile; explicitly accepted unencrypted content/key transport; peer isolation can still block it | Home HTTP local checks pass; no iPhone test |
| Laptop-created private access point — optional | No | iPhone joins a separate Wi-Fi network; requires supported laptop hardware/drivers; simultaneous school Wi-Fi and phone internet behavior need testing | Adapter reports Wi-Fi Direct GO support; actual AP/iPhone test not run |
| iPhone Personal Hotspot, including USB tethering | No, if explicitly using the tether interface | Depends on Personal Hotspot availability; Windows USB use requires Apple Devices or iTunes; phone-to-receiver reachability must be demonstrated | Apple documents tethering, not this clipboard flow |
| Custom Bluetooth LE app | No | Requires an iPhone app or compatible existing app; does not preserve the native-Shortcuts-only setup; photo performance and app distribution need investigation | Apple documents app-based BLE data transfer; no prototype |

An internet relay would change the original no-cloud/no-internet scope and has not been selected.

## Optional approach: private Windows access point

Windows' [WiFiDirectLegacySettings](https://learn.microsoft.com/en-us/uwp/api/windows.devices.wifidirect.wifidirectlegacysettings) supports a normal Wi-Fi access point for devices that do not implement Wi-Fi Direct themselves. The iPhone would join through Wi-Fi settings; this is not a claim that iOS Shortcuts directly supports Wi-Fi Direct APIs.

Microsoft's [official sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/wifi-direct-legacy/) requires a Wi-Fi Direct capable card and driver. Its access point does not provide internet cross-connectivity. This makes it a candidate for a local service without forwarding school traffic, but it is not proof of compatibility with this laptop or iPhone.

Do not confuse this with the ordinary [Windows Mobile Hotspot feature](https://support.microsoft.com/en-us/windows/experience/connectivity-networking/use-your-windows-device-as-a-mobile-hotspot), which is documented as sharing an existing internet connection. Also, Windows Mobile Hotspot and Wi-Fi Direct scenarios can conflict; Microsoft says Mobile Hotspot takes precedence.

Manual checks still needed for this prototype:

1. Establish that the laptop's adapter and driver support the private AP scenario.
2. Start a temporary AP with the prototype when the user is ready. An automatic startup/check/shutdown run was rejected by approval review, so it was not executed.
3. Join with the actual iPhone and confirm that it can reach a minimal laptop service.
4. Test without upstream internet and check whether laptop school Wi-Fi remains usable.
5. Check iPhone internet behavior while attached and whether the extra network-switching step is acceptable.
6. Confirm the intended environment permits the connection method.
7. Exercise the implemented authenticated HTTPS receiver with text, images, and history on the actual phone/laptop pair.

## Other connection details

Apple documents [USB Personal Hotspot on Windows](https://support.apple.com/guide/iphone/share-your-internet-connection-iph45447ca6/ios) with Apple Devices or iTunes. That is a network tether, not a generic Shortcuts USB clipboard API. Using a receiver over this connection is an inference that requires testing; it must not be advertised as working yet.

Apple's [BLE data-transfer sample](https://developer.apple.com/documentation/corebluetooth/transferring-data-between-bluetooth-low-energy-devices) demonstrates an app-based implementation. A custom BLE solution would require revisiting the preference to avoid a custom iPhone app and considering signing/distribution before committing to it.

## Prototype security design

The receiver uses one revocable 256-bit phone key in every mode. Home HTTP deliberately omits TLS, serves no certificate profile, and requires no iPhone trust setup. Optional HTTPS uses a per-installation local certificate authority. Identity and history remain DPAPI-protected locally. This prototype has one pairing slot; revocation invalidates all Shortcuts using that key. See [Shortcut onboarding](shortcut.md).

[Apple requires explicit trust](https://support.apple.com/en-us/102390) for manually installed certificate profiles. Installing a root certificate grants trust beyond a single app, so onboarding must explain that scope, protect the private key on Windows, provide an independently verified public-certificate delivery method, and document removal. A certificate downloaded from an unauthenticated local page must not be trusted without verification. Never disable TLS verification to make setup appear to work.

A private access point does not eliminate application authentication. In HTTPS modes only, HTTP serves public onboarding material and rejects transfers; in the explicitly selected home HTTP mode, HTTP receives authenticated but unencrypted transfers.

## Validation performed

- Inspected an empty project directory and available development tools.
- Installed the official .NET 10.0.403 SDK into the ignored project-local `.tools/dotnet` directory and verified its version.
- Researched the platform options using the official sources linked above.
- Built the .NET 10 Windows Forms text/photo receiver and passed 55 local HTTP/HTTPS, storage, validation, retry, and retention checks using a simulated clipboard. Rendered and inspected the home-mode window.
- Inspected adapter capabilities: Wi-Fi Direct Device, GO, and Client are supported; legacy Hosted Network and Soft AP are not. This does not prove AP startup or concurrent school Wi-Fi operation.
- An automated AP startup/check/shutdown command was rejected by approval review; no access point was started and no iPhone connection was tested.
- Did not change firewall rules or saved network settings, or create a remote repository. Generated only disposable test identities under ignored `.tools`; the actual application creates its identity when launched by the user.
- The .NET SDK's first-run restore reported generating a development HTTPS certificate. The probe does not use it, and no certificate trust was enabled. Project scripts suppress further first-run certificate generation.

The source targets .NET 10 LTS when implementation proceeds. [.NET 8 support ends November 10, 2026](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), so the preinstalled .NET 8 SDK is not a suitable long-term starting point for this new project.
