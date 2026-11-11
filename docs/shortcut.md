# iPhone: Send to Laptop

This manual recipe uses native Shortcuts actions and incorporates the user's real iPhone setup feedback. No importable `.shortcut` file is supplied: this environment cannot sign and validate one. Labels/type names can vary with iOS language/version; the recipe uses English.

The user has since reported successful home HTTP text/photo transfers after the adjustments below. For the new Tailscale connection, follow [tailscale.md](tailscale.md): change both POST URLs to the laptop's stable Tailscale IP and keep the working payload/header setup.

## House Wi-Fi setup — no profile

This is the selected prototype workflow:

1. Connect the iPhone and laptop to the same house network. A laptop connected by Ethernet to that router also works if the router permits communication with Wi-Fi clients.
2. Launch EasyClipboard, leave **Home Wi-Fi / LAN — HTTP** selected, select the laptop's active Wi-Fi/Ethernet address, and click **Start receiving**.
3. Open **Pairing / setup**. Use the **Shortcut URL** from the main window and the `Bearer <key>` header from the pairing window.
4. Continue with **Create the Shortcut** below. Use `http://<laptop-IP>:47831/v1/transfers` exactly as displayed. Do not install a profile or enable SSH for this mode.

The pairing key is still required and revocable. HTTP sends it and clipboard content unencrypted over the network; this is the explicitly selected simpler prototype mode. Local storage remains DPAPI-encrypted. Don't publish the key or share a Shortcut containing it.

If the laptop's address changes, update the Shortcut URL and scoped firewall rule. A DHCP reservation on your home router can keep the laptop address stable; no port forwarding is needed. If the phone cannot reach the laptop, check [setup.md](setup.md), including guest Wi-Fi isolation.

## Optional HTTPS certificate setup — skip for home HTTP

1. For home HTTPS, choose **Home Wi-Fi / LAN — HTTPS**, select the home interface, and **Start receiving**. For private hotspot HTTPS, choose that mode and **Start hotspot**, keeping Windows Mobile Hotspot off. Open **Pairing / setup**.
2. For home HTTPS, keep the iPhone on the house Wi-Fi. For private hotspot HTTPS, join the exact Wi-Fi name/password from the app. Stay connected if iOS reports no internet; that private link does not forward internet.
3. In Safari, open the **Certificate install** URL shown on Windows: `http://<AP-IP>:47831/install.mobileconfig`. It contains only a public root certificate. HTTP is used for initial public onboarding material, not transfers.
4. Before trusting it, inspect the certificate actually contained in that downloaded profile. Compare its **entire SHA-256 fingerprint** with **Pairing / setup** on the laptop. A matching name is insufficient. The certificate-detail UI varies by iOS version. If yours does not expose a usable fingerprint, do not guess: securely deliver and verify the exact profile through an already trusted file-transfer method first. Verifying a separately downloaded `.cer` does not authenticate a different profile download.
5. Install the verified profile from **Settings → General → VPN & Device Management** (or **Profile Downloaded**), following the passcode/install prompts.
6. Enable SSL trust under **Settings → General → About → Certificate Trust Settings** for this exact EasyClipboard root. Profile installation alone does not enable TLS trust. [Apple documents this requirement](https://support.apple.com/en-ie/102390).
7. Record the app's **Shortcut URL**. Put the displayed `Bearer <key>` value in the Shortcut Authorization header below. This is secret; never put it in a URL, repository, shared Shortcut, or screenshot.

Trust tradeoff: a private CA needs no domain, account, cloud, or renewal service, but grants the iPhone broader certificate trust than one app. Its signing key stays DPAPI-protected on this Windows account; a compromised account/laptop could misuse it. A public certificate avoids manual root trust but adds domain/certificate infrastructure. This prototype uses the private CA and never bypasses TLS validation.

A 90-day server certificate is issued for the discovered AP IP when the receiver starts. If the IP changes, update the Shortcut URL; the root remains the same. Keep clocks correct. The root expires after five years and requires replacement/re-pairing; automatic rotation is not implemented.

## Create the Shortcut

Create a Shortcut named **Send to Laptop**. Use magic variables for values below. Do not interpolate copied text into a hand-written JSON Text action; use the JSON body fields so quotes/newlines are escaped correctly.

1. **Get Clipboard**.
2. **Set Variable** `Item` to **Clipboard**.
3. **If Item has any value**: put the remaining actions in this branch. In its **Otherwise**, add **Show Alert** (`Clipboard is empty`), then **Stop This Shortcut**.
4. **Get Type**, input `Item`; its result is `ItemType`.
5. Generate a transfer ID with native actions (correction: **Generate UUID** is supplied by third-party apps on many iOS versions):
   - **Current Date**.
   - **Set Variable** `TransferID` to the output of **Current Date**.
   - Tap that Date input in the Set Variable action, and use its variable popup's **Date Format → Custom** setting with pattern `yyyyMMdd-HHmm-ssSS-SSS0-000000000000`. This formats the value to text directly; do not then feed that custom text through a second Format Date action.
   - This produces a GUID-shaped timestamp accepted by this prototype, distinct per millisecond for normal single-phone use. It is not a random UUID; simultaneous invocations in the same millisecond can collide. Create it once per press and retain it for retries.
6. **If ItemType is Image OR Photo media** (Any condition, not All; the user's Photos copy reports Photo media):
   - **Convert Image**, input `Item`, format **PNG**. Disable **Preserve Metadata** if shown. This explicitly converts actual HEIC/JPEG image input into the receiver's PNG format.
   - **Base64 Encode**, input **Converted Image**, mode **Encode**, **Line Breaks: None**.
   - **Set Variable** `Payload` to the encoded output.
   - **Get Contents of URL**, using the exact **Shortcut URL** shown on Windows: `http://<laptop-IP>:47831/v1/transfers` in home HTTP mode, or the displayed HTTPS URL in HTTPS modes. Set method **POST**. Under **Headers**, set `Authorization` to `Bearer <key>` and `Content-Type` to `application/json` if not already set by the JSON body action.
   - Under **Request Body → JSON**, add the following typed fields:

     | Key | Shortcuts field type | Value |
     | --- | --- | --- |
     | `version` | Number | `1` |
     | `transferId` | Text | `TransferID` variable |
     | `contentType` | Text | `image/png` |
     | `dataBase64` | Text | `Payload` variable |

   - **Set Variable** `Response` to **Contents of URL**.
7. In the image If's **Otherwise**:
   - Accept only `ItemType` **Text**, **Rich text**, or **URL**. Use an Any-condition If if available, or nested Ifs to reject an item only when it matches none of those three. For unsupported types, **Show Alert** (`Unsupported clipboard type: [ItemType]. Copy one text item or an actual image.`), then **Stop This Shortcut**.
   - **Get Text from Input**, input `Item`, then **Set Variable** `Payload` to the result. URLs are sent as text. Do not add Get Images from Input or a webpage/URL-download action here.
   - Add a second **Get Contents of URL** with the same app URL, POST method, headers, and these **Request Body → JSON** fields:

     | Key | Shortcuts field type | Value |
     | --- | --- | --- |
     | `version` | Number | `1` |
     | `transferId` | Text | `TransferID` variable |
     | `contentType` | Text | `text/plain` |
     | `text` | Text | `Payload` variable |

   - **Set Variable** `Response` to that action's output.
8. After that **End If**, use **Get Dictionary from Input** on `Response` if it was not already interpreted as a dictionary.
9. Use **Get Dictionary Value** to read `ok`, `saved`, and `clipboardUpdated`. Add nested **If** checks that each equals Boolean true (not merely “has any value”). Only inside all three true branches add **Show Notification** (`Sent to Laptop — saved and ready to paste`).
10. On a missing/false value, **Show Alert** with `Response`, then stop. Do not put a success notification after the If branches unconditionally.

The receiver sends success only after history and clipboard completion. A successful duplicate retry also returns all three true flags. Native Shortcuts may itself stop Get Contents of URL on network/HTTP failure before the dictionary checks run; that is failure, not successful delivery. This recipe has no general-purpose try/catch wrapper. Error-body availability depends on iOS behavior.

Apple documents [JSON POST bodies](https://support.apple.com/en-gb/guide/shortcuts/apd58d46713f/ios) and [input types](https://support.apple.com/en-gb/guide/shortcuts/apd7644168e1/ios). Source apps expose different representations. The user's Photos copy reports **Photo media**, so it is accepted alongside **Image**. **File** and other types are not silently downloaded/coerced. Use this branch for copied photos; video transfer is outside the prototype, and a broad media type is not proof that an item is a supported photo. Universal iOS clipboard compatibility is not claimed.

## Use

- Run once from Shortcuts while unlocked and allow clipboard/paste, local-network, and endpoint-access prompts. If denied, check **Settings → Privacy & Security → Local Network** and the Shortcut's privacy settings; labels vary by iOS version.
- Add it through the Shortcut menu → **Add to Home Screen**.
- On iOS versions offering Shortcut controls, edit Control Center → **Add a Control → Shortcuts** and select **Send to Laptop**. Use Home Screen as fallback.
- Copy one text item or actual photo, invoke once, wait for confirmation, then Ctrl+V in an appropriate Windows app. For images, try Paint or another bitmap-capable app.
- The receiver copies PNG content at one source pixel per destination pixel, independent of DPI metadata. Its history preview fits the window without changing the stored image. An earlier receiver bug used DPI-based physical sizing and could shrink content inside an unchanged canvas; after updating, resend the original screenshot with a new transfer ID. Copy Again or a retry of the old ID cannot restore pixels already lost in an old history entry.
- Send images at their original resolution; no Resize Image action is required. The former 12 MB, 20-megapixel and 12000-pixel-per-side cutoffs are removed. PNG conversion can increase file size, so large images take longer to upload. Processing depends on available PC memory and Windows PNG-decoder support. Images are validated and re-encoded without changing their dimensions or pixels; metadata is not preserved. The receiver does not accept HEIC directly.

## Errors, retries, and removal

| Failure | Action |
| --- | --- |
| Laptop asleep, app stopped, or unreachable | Wake/start it, connect both devices to the selected network, verify the current URL and scoped firewall rule. |
| Certificate/TLS error in optional HTTPS mode | Check verified root trust, clocks, and current IP. Never bypass TLS validation. Home HTTP is a separate explicitly selected mode, not an automatic fallback. |
| 401 | Read the specific code: `authorization_missing` means the header was absent; `authorization_format` means its scheme/value layout is invalid; `pairing_key_length` reports the received length; `pairing_key_format` means non-hex characters; `pairing_key_mismatch` means the header format was valid but the key differs from the running receiver. The key itself is never included in these diagnostics. |
| 503 paused / clipboard_busy | Unpause or wait for clipboard contention to clear, then retry. |
| 503 image_memory_unavailable / upload_memory_unavailable | Close memory-heavy apps on the PC and retry; the receiver never automatically downsizes images. |
| 400 invalid_image | Send actual PNG image data using the existing Convert Image action. |

For an uncertain response, retry using the **same TransferID and payload**. Simply pressing the Shortcut button again creates a new transfer intentionally. To add manual Retry, repeat the HTTP action behind a Retry choice while keeping this invocation's variables. Automatic retries are not included in this basic recipe.

**Revoke key and create a new one** immediately invalidates the old key for subsequent requests. This prototype has one pairing slot. On retirement, delete the Shortcut and remove the scoped firewall rule from [setup.md](setup.md). If you previously used HTTPS, remove its EasyClipboard certificate profile under **VPN & Device Management**; home HTTP requires none.
