# Installing Yoe Yar POS on clients' devices

## A. Once, before the first client

1. **Host the licence server** with HTTPS. See `LICENSING.md` → "Before you sell".
2. **Point the app at it:** in `FreshMart.Pos/Services/LicenseService.cs` set
   `DefaultServer = "https://license.your-domain.com"`.
3. **Create the Android signing key** (once, ever). Every update must be signed with the same key: if it is lost, shops must
   uninstall the app (losing their data unless they restore a backup) before they can install a new version.
   ```powershell
   & "C:\Program Files\Android\openjdk\jdk-21.0.8\bin\keytool.exe" -genkeypair -v -keystore C:\Keys\yoeyar.keystore -alias yoeyar -keyalg RSA -keysize 2048 -validity 10000
   ```
   Keep `C:\Keys\yoeyar.keystore` and its password **outside this project folder**, with two offline backups.
4. **Build the release files** (about 3 minutes):
   ```powershell
   $env:YOEYAR_KEYSTORE = "C:\Keys\yoeyar.keystore"
   $env:YOEYAR_KEYSTORE_PASS = "<your keystore password>"
   .\build-release.ps1
   ```
   You get `release\YoeYarPOS-android.apk` and `release\YoeYarPOS-windows-x64.zip`. The script warns you if the
   signing key or server address is missing.

## B. For each new shop

1. In your admin panel: **+ New shop**. Enter the name as it should print on receipts, choose the plan, and copy the **shop code** (`YY-XXXX-XXXX`).
2. Install the app on the shop's device (below).
3. With the internet on, open Yoe Yar POS and type the shop code. Then the owner creates their name and PIN.
4. Help them enter products (or import), customers and opening debts. Make the first backup together (Settings → Data).

### Android phone or tablet (Android 7.0 or newer)
1. Send `YoeYarPOS-android.apk` to the phone: Viber/Telegram to the owner, a Google Drive link, or a USB cable.
2. Tap the file. When Android asks, allow **"Install unknown apps"** for that app (Viber, Files, Chrome…), then tap **Install**.
3. If Play Protect warns "unrecognised developer", tap **More details → Install anyway**. That is normal for apps outside the Play Store.
4. Open **Yoe Yar POS** from the home screen and type the shop code.

### Windows PC or laptop (Windows 10 or 11, 64-bit)
1. Copy `YoeYarPOS-windows-x64.zip` to the PC (USB stick, Google Drive, Viber Desktop).
2. Right-click → **Extract All**, open the folder, double-click **Install Yoe Yar POS**.
3. If Windows shows **"Windows protected your PC"**, click **More info → Run anyway** (no code-signing certificate yet).
4. The app opens and a desktop shortcut is added. Type the shop code.

No .NET or other software is needed. Old Windows 10 PCs without the **Microsoft Edge WebView2 Runtime** show a blank window:
install it from Microsoft's "WebView2 Runtime" download page (Evergreen Bootstrapper), then open the app again.

## C. Updates

1. Raise the version in `FreshMart.Pos/FreshMart.Pos.csproj` (`ApplicationDisplayVersion` 1.1, `ApplicationVersion` 2 …).
   Android refuses an update with the same or lower `ApplicationVersion`.
2. Run `build-release.ps1` with the **same** keystore.
3. Android: send the new APK, tap it, tap **Update**. Windows: extract the new zip and run **Install Yoe Yar POS** again.
   Sales, debts, staff and the licence are kept in both cases.

## D. Common support jobs

| Situation | What to do |
|---|---|
| Shop gets a new phone | Make a backup on the old phone (Settings → Data). Remove the old device in the admin panel, install on the new phone, type the shop code, then restore the backup. |
| "Device limit reached" | The plan's devices are used. Remove a device in the admin panel or upgrade the plan. |
| "Cashier accounts are full" | Upgrade the plan, then in the app Settings → Licence → **Check now**. |
| Phone was factory-reset | It counts as a new device: remove the old entry in the admin panel. |
| App says "not online for 30 days" | Connect to the internet and tap Settings → Licence → **Check now**. |

## E. Later: easier distribution
- **Google Play:** USD 25 one-time developer account; upload an `.aab` (`-p:AndroidPackageFormats=aab`) signed with the same key.
  Shops then install and update from Play with no "unknown apps" step.
- **Windows code-signing certificate** (about USD 200–400 a year) removes the "Windows protected your PC" warning.

## Note for the old FreshMart test installs
The app ID changed to `com.yoeyar.pos`, so Android installs it as a **new app** next to any old FreshMart POS test install.
Back up in the old one first if it holds real data, restore in Yoe Yar POS, then uninstall the old one.
On this Windows PC, the first start of Yoe Yar POS copies the old data across automatically. Close the old app first.
