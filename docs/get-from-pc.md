# iPhone: Get from PC

Copy on Windows, tap **Get from PC** on iPhone, then paste. This is an on-demand fetch of the current PC clipboard, with the existing pairing key and network connection. It does not monitor or change the PC clipboard, add history entries, or automatically push anything to iPhone. Text and actual clipboard images are supported; copied file paths/files are not imported.

## Windows setup

1. Quit EasyClipboard from its tray menu and reopen **Start EasyClipboard.cmd** to load the update.
2. Select your working network/mode and click **Start receiving**.
3. Check **Allow phone to fetch PC clipboard**. This permission starts off each time receiving starts. Stop, Quit, or unchecking the option revokes it; disabling and re-enabling also invalidates an older in-flight fetch.
4. Use the **Get from PC URL** displayed in the setup box. It uses the same address and port as Send to Laptop, with `/v1/clipboard` instead of `/v1/transfers`. Keep using the proven Wi-Fi connection; this feature does not resolve the separate Shadowrocket cellular problem.

The existing HTTP/HTTPS/Tailscale transport choice applies in both directions. Home HTTP carries content and the pairing key unencrypted on the LAN, as in the existing prototype. HTTPS modes require HTTPS for fetching, and Tailscale mode carries HTTP inside the tunnel. The download endpoint requires the same `Authorization: Bearer <pairing-key>` header as sending. Do not put credentials in the URL or share a Shortcut containing them.

## Native Shortcut

Create a **new, blank** Shortcut named **Get from PC**. The three main actions are request, copy, and notification. Include the small error branch below because Get Contents of URL may return an HTTP error dictionary as content rather than stopping the Shortcut.

1. **Get Contents of URL**:
   - URL: the exact **Get from PC URL** displayed by EasyClipboard.
   - Method: **GET**.
   - Headers: left field **Authorization**, right field the existing **Bearer &lt;pairing-key&gt;** value.
   - No request body, JSON fields, Base64 action, or transfer ID.
2. **Get Type**, input explicitly **Contents of URL** from step 1.
3. **If** the Type **is** the text `Dictionary`:
   - **Show Alert**, input explicitly **Contents of URL** from step 1. This displays the server's error.
4. **Otherwise**:
   - **Copy to Clipboard**, input explicitly **Contents of URL** from step 1. Do not choose Type or If Result.
   - **Show Notification**: `Copied from PC`.
5. **End If**. Keep Copy to Clipboard and the success notification inside Otherwise.

The server sends successful text as `text/plain; charset=utf-8` and images as actual `image/png` bytes, with an inline `clipboard.png` filename. There is no successful JSON envelope or Base64 decoding step. Labels/content types may vary with iOS version; if an image arrives as a file and cannot be pasted, inspect its output type before adding any conversion. Real iPhone text/image paste must be verified rather than assumed from the server tests. Apple documents [Get Contents of URL](https://support.apple.com/en-ph/guide/shortcuts/apd58d46713f/ios) and [Copy to Clipboard](https://support.apple.com/en-gb/guide/shortcuts/apd081d9d61f/ios).

## First tests

1. Copy one short line of text on the PC. Run Get from PC and paste into Notes on iPhone.
2. Copy an actual image on the PC (for example, a screenshot or an image copied from Paint). Run Get from PC and paste into an image-capable app on iPhone. PNG dimensions and pixels are preserved independently of input DPI; limits remain 12 MB and 20 megapixels.
3. Uncheck the PC fetch option and try the Shortcut. It should show the error instead of overwriting the iPhone clipboard or showing success. Re-enable when ready.

The phone must reach the PC, and the PC must be awake. The existing Send to Laptop Shortcut remains unchanged. **Pause receiving** controls incoming transfers; use the separate fetch checkbox to disable outgoing access.

## Local validation

22 reverse-transfer checks cover opt-in, authentication before reads, fresh Unicode text, raw PNG/MIME, pixel preservation, no history or clipboard writes, invalid/oversized input, concurrent/cancelled requests, consent withdrawal during a fetch, revoked keys, method restrictions, and HTTPS transport enforcement. Clipboard source/sink are simulated; actual Windows clipboard capture and native iPhone copying still require the first tests above.
