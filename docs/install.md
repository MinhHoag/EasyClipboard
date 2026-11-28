# Set up your iPhone

Three Shortcuts connect your iPhone to EasyClipboard. Install them **in this order**.

**Time:** about 10 minutes · **Before you start:** finish [Set up Tailscale](tailscale-setup.md).

<p align="center"><img src="images/setup-steps.svg" width="780" alt="Six steps: 1 turn on Taildrop in the Tailscale admin console, 2 show the setup code on the PC, 3 install First time Setup on the iPhone and scan the code, 4 install ECSend, 5 install ECReceive, 6 test it."></p>

> [!IMPORTANT]
> **Tap "Always Allow" on first run.** The first time each Shortcut reads your clipboard, pastes, sends to your PC or saves your info, iPhone asks for permission. Almost every step asks once. Tap **Always Allow** each time. If you tap **Allow Once**, it asks again on every run.

> [!IMPORTANT]
> **Install "First time Setup" first.** ECSend and ECReceive use what it saves. If you install them first, your iPhone won't find it and the Shortcuts won't work.

| Order | Shortcut | What it does | Install |
| :---: | --- | --- | --- |
| 1 | **First time Setup** | Picks your PC for Taildrop, scans the setup code, and keeps it all in one place | [Get Shortcut][settings] |
| 2 | **ECSend** | Sends what you copied on iPhone to the PC | [Get Shortcut][push] |
| 3 | **ECReceive** | Brings what you copied on the PC to your iPhone | [Get Shortcut][pull] |

---

## Step 1. Turn on Taildrop

**Don't skip this.** Photos travel through Taildrop. Taildrop is still an alpha feature, so it's **off** until you opt in ([Tailscale's docs](https://tailscale.com/docs/features/taildrop#enable-taildrop-for-your-network)). If it's off, nothing tells you: photos just never arrive.

1. On any device, open the admin console's [General settings](https://console.tailscale.com/admin/settings/general) page.
2. Turn on **Send Files**.

<p align="center"><img src="images/guide/taildrop-send-files.svg" width="560" alt="Admin console with the Send Files setting turned on"></p>

**Check it works:** on your iPhone, open a photo, tap **Share → Tailscale** (it may be under **More** the first time), and pick your PC. The photo should land in your PC's **Downloads** folder. If it doesn't, stop here and see [If something doesn't work](#if-something-doesnt-work).

<p align="center"><img src="images/guide/share-tailscale.svg" width="260" alt="iPhone share sheet with the Tailscale option highlighted"></p>

> [!TIP]
> **"Tailscale is not running"?** Open the **Tailscale** app, wait until it says connected, then come back and share the photo again.

> [!NOTE]
> Screenshot to add: the photo arriving in Downloads on the PC.
<!-- SCREENSHOT: pc-taildrop-downloads.png -->

## Step 2. Show the setup code on your PC

1. Open **EasyClipboard** on your PC.
2. Pick your **Tailscale** address in the network list, then press the green **play** button. The dot under the name turns green.
3. Click **Pairing / setup**. The QR code is at the bottom left. **Setup values** above it lists the same SendURL, ReceiveURL and Auth Key as text, and **Copy all** copies the whole code.

| Line | What it is |
| :---: | --- |
| 1 | `EasyClipboard-1`, so the Shortcut knows it scanned the right code |
| 2 | **SendURL**: where ECSend sends things |
| 3 | **ReceiveURL**: where ECReceive fetches from |
| 4 | **Auth Key**: your pairing key, 64 characters, only 0–9 and A–F |

> [!TIP]
> **"Tailscale not connected" next to the network list?** Open **Tailscale** from the PC's system tray, connect it, then press the refresh button next to the list.

> [!NOTE]
> Screenshot to add: Pairing / setup with the setup code at the bottom left.
<!-- SCREENSHOT: pc-setup-code.png -->

> [!CAUTION]
> The setup code contains your pairing key, which works like a password for your clipboard. Don't post it or share a screenshot of it. If it gets out, click **Replace pairing key**; the old code stops working and a new one appears.

## Step 3. Install First time Setup

You run this once, on first use. It pairs your iPhone with your PC.

1. Tap **[Get Shortcut][settings]** on your iPhone, then **Add Shortcut**.
2. Run **First time Setup**. Each step below starts with the heading your iPhone shows.
3. **Pick Your PC that installed EasyClipboard**: choose your PC. Taildrop also sends your photos there.

<p align="center"><img src="images/guide/setup-pick-pc.svg" width="240" alt="Pick Your PC that installed EasyClipboard, with your PC highlighted"></p>

4. **Setup Instructions**: tap **OK**, then point the camera at the QR code in Pairing / setup on your PC.

<p align="center"><img src="images/guide/setup-instructions.svg" width="240" alt="Setup Instructions asking you to scan the QR code, with OK highlighted"></p>

5. **PC Name, SendURL, ReceiveURL, Auth Key**: check they match **Setup values** on your PC, then tap **Done**.

<p align="center"><img src="images/guide/setup-details.svg" width="240" alt="The values read from the QR code, with Done highlighted"></p>

6. **Are these details correct?**: tap **Save**.

<p align="center"><img src="images/guide/setup-save.svg" width="240" alt="Are these details correct? with Save highlighted"></p>

7. Tap **Always Allow** on any permission prompts.

That's it. ECSend and ECReceive use these saved values from now on, so you don't need to pick or scan again.

> [!TIP]
> **"Tailscale is not running"?** Open the **Tailscale** app, wait until it says connected, then come back and run "First time Setup" again.

> [!TIP]
> Can't scan? Click **Copy all** on the PC, get the text onto your iPhone (for example by saving it to a file and choosing **Send with Tailscale**), copy it there, then run "First time Setup". It reads the same four lines from the clipboard.

> [!NOTE]
> Screenshot to add: First time Setup after a successful scan.
<!-- SCREENSHOT: phone-settings-scan.png -->

**Changed something on the PC?** If you revoke the key or your PC gets a new address, just run "First time Setup" again and scan the new code. You don't need to touch ECSend or ECReceive.

## Step 4. Install ECSend

> [!TIP]
> It's easier if you open this page in GitHub mobile, or send yourself the [Shortcut links](#shortcut-links) at the end of this page.

Tap **[Get Shortcut][push]**, then **Add Shortcut**. ECSend reads your values from "First time Setup" and sends photos through Taildrop, which is why that comes first.

> [!TIP]
> **"Tailscale is not running"?** Open the **Tailscale** app, wait until it says connected, then come back and run ECSend again.

## Step 5. Install ECReceive

> [!TIP]
> It's easier if you open this page in GitHub mobile, or send yourself the [Shortcut links](#shortcut-links) at the end of this page.

Tap **[Get Shortcut][pull]**, then **Add Shortcut**. ECReceive also reads your values from "First time Setup".

> [!TIP]
> **"Couldn't connect" or a timeout?** Tailscale on your iPhone is probably asleep. Open the **Tailscale** app, wait until it says connected, then come back and run ECReceive again.

> [!NOTE]
> ECReceive only works while **Phone fetch** is on in EasyClipboard (Settings → Phone fetch). It's on by default.

## Step 6. Test it

<p align="center"><img src="images/guide/test-flow.svg" width="760" alt="Test: Send copies from iPhone to PC in four steps; Receive brings PC content back to the iPhone in four steps"></p>

1. **Send:** copy some text on your iPhone, run **ECSend**, then press **Ctrl+V** on the PC.
2. **Photos:** copy a photo and run ECSend. It arrives through Taildrop and lands on the PC clipboard.
3. **Receive:** copy something on the PC, run **ECReceive**, then paste on your iPhone.

> [!TIP]
> **"Tailscale is not running"?** Open the **Tailscale** app, wait until it says connected, then come back and run the Shortcut again.

Done.

## Put it one tap away

Pick where you want ECSend and ECReceive. You can use more than one.

| I want to… | Jump to |
| --- | --- |
| Press a button on the side of my iPhone | [Action button](#action-button) |
| Tap it from Control Center | [Control Center](#control-center) |
| Tap an icon on my home screen | [Home screen](#home-screen) |
| Tap the back of my phone | [Back Tap](#back-tap) |
| Pick Send or Receive from one menu | [One menu for both](#one-menu-for-both) |

### Action button

On iPhone 15 Pro and later.

**One Shortcut:** open **Settings → Action Button**, swipe to **Shortcut**, tap the name under it, and pick **ECSend**.

<p align="center"><img src="images/guide/action-button-ecsend.jpg" width="240" alt="Action Button set to the ECSend Shortcut"></p>

**All three on one button:** put the Shortcuts in a folder and pick the folder instead. No extra Shortcut needed.

1. In **Shortcuts**, tap **Select**, tick **ECSend**, **ECReceive** and **First time Setup**, then tap the folder button at the bottom.

<p align="center"><img src="images/guide/shortcuts-select.svg" width="240" alt="The three EasyClipboard Shortcuts selected, with the folder button highlighted"></p>

2. Tap **New Folder** and name it **EasyClipboard**.

<p align="center"><img src="images/guide/shortcuts-new-folder.svg" width="240" alt="Select a Folder sheet with New Folder highlighted"></p>

3. In **Settings → Action Button**, swipe to **Shortcut**, tap the name under it, and pick your **EasyClipboard** folder. It then reads **Show** followed by the folder's name.

<p align="center"><img src="images/guide/action-button-show-folder.jpg" width="240" alt="Action Button set to Shortcut, showing a folder of Shortcuts"></p>

Pressing the Action button now opens the folder, and you tap the one you want.

<p align="center"><img src="images/guide/action-button-folder.svg" width="240" alt="The Action button opening the EasyClipboard folder with ECSend, ECReceive and First time Setup"></p>

### Control Center

On iOS 18 and later.

1. Swipe down from the top-right corner to open Control Center.
2. Tap **+** at the top left, then **Add a Control**.
3. Search for **Shortcut**, tap it, and choose **ECSend**.
4. Repeat for **ECReceive**.

### Home screen

1. In **Shortcuts**, press and hold **ECSend**.
2. Tap **Share**, then **Add to Home Screen**.
3. Optionally rename it to **Send** and pick an icon, then tap **Add**.
4. Repeat for **ECReceive**, or add just the menu Shortcut below.

<p align="center"><img src="images/guide/home-screen.svg" width="240" alt="Home screen with Send and Receive icons"></p>

You can also add a **Shortcuts widget** to your home screen to show several Shortcuts at once.

### Back Tap

Double-tap or triple-tap the back of your iPhone to run a Shortcut.

1. Open **Settings → Accessibility → Touch → Back Tap**.
2. Choose **Double Tap** or **Triple Tap**.
3. Scroll to **Shortcuts** and pick **ECSend** or **EasyClipboard Menu**.

### One menu for both

For the Action button, the folder above is simpler. For a home screen icon or Back Tap, make a small Shortcut that asks "Send or Receive?":

1. In **Shortcuts**, tap **+** to make a new Shortcut. Name it **EasyClipboard Menu**.
2. Add the **Choose from Menu** action. Set the prompt to *What do you want to do?* and the two items to **Send to PC** and **Receive from PC**.
3. Under **Send to PC**, add **Run Shortcut** and pick **ECSend**.
4. Under **Receive from PC**, add **Run Shortcut** and pick **ECReceive**.
5. Optional: tap the Shortcut's icon to pick a color and symbol.

<p align="center"><img src="images/guide/shortcut-menu.svg" width="240" alt="A menu asking Send to PC or Receive from PC"></p>

---

### If something doesn't work

| What you see | Try this |
| --- | --- |
| "The action could not run because Tailscale is not running" | Open the **Tailscale** app, make sure it's connected, then run the Shortcut again. Keeps happening? On iPhone go to **Settings → Tailscale** and turn on **Background App Refresh**. |
| It keeps asking for permission | You tapped **Allow Once**. Run it again and tap **Always Allow**. |
| "Couldn't connect" or a timeout | Is Tailscale on for both devices? Is EasyClipboard showing a green dot? |
| "…does not match this running receiver's pairing key" | The saved key is old. Run "First time Setup" and scan the current setup code. |
| "…this receiver expects 64" | Extra spaces or the word "Bearer" got pasted into the key. Paste only the 64-character code. |
| ECSend or ECReceive asks you to pick a Shortcut | You installed them before "First time Setup". Install it, or reinstall in order. |
| Photos don't arrive | Redo the photo check in Step 1. If that fails, **Send Files** is off or Tailscale isn't connected. If it works, run "First time Setup" again and pick the right PC. |
| "Turn on Settings → Phone fetch" | Turn on **Settings → Phone fetch** in EasyClipboard, and make sure it's receiving (green dot). |
| You edited a Shortcut and now it's broken | Compare it with [Inside the Shortcuts](shortcut-structure.md), or reinstall it from the links below. |

### Shortcut links

Send these to your phone, then install them in this order:

1. [First time Setup][settings]
2. [ECSend][push]
3. [ECReceive][pull]

[settings]: https://www.icloud.com/shortcuts/7ab6895c90f8479d86a64ca52923667d
[push]: https://www.icloud.com/shortcuts/0719dbb9ce2b48fe99b8dc46134c4bdb
[pull]: https://www.icloud.com/shortcuts/19b847527a3542feac2368eebd63b911
