# Putting the Yoe Yar POS licence server on Z.com

About 30 minutes of work, plus waiting for Z.com to confirm your payment.
You need: this project on your PC, and your `license-signing-key.pem` (see `LICENSING.md`).

## 1. Order (Z.com website)

1. Make an account at https://cloud.z.com/mm/ (phone number and email).
2. **Pay** for the services below by KBZ Pay, KBZ or CB transfer, and email the bank slip with your Z.com Account ID to their billing address.
3. **Cloud VPS:** Standard **C1** (1 core, 2 GB), OS **Ubuntu** (the newest version offered), monthly.
   Auto Backup **Basic** is worth adding if it is cheap.
4. **Domain** at https://domain.z.com/mm/, for example `yoeyar.com`.
5. When the VPS is ready, write down its **IP address** and **root password**. Keep them private.

Don't buy Share Hosting: it cannot run the licence server.

## 2. Point your domain at the server

In Z.com's domain panel → DNS, add:

| Type | Name | Value |
|---|---|---|
| A | `license` | your VPS IP address |

That makes `license.yoeyar.com` point to your server. It can take from a few minutes up to a few hours.
Check it on your PC (it should print your VPS IP):
```powershell
Resolve-DnsName license.yoeyar.com
```

## 3. Install everything (one command, on your PC)

In PowerShell, in the project folder:
```powershell
.\deploy\publish-server.ps1 -Server <VPS IP> -FirstTime -Domain license.yoeyar.com -Support "Viber 09-xxx-xxx-xxx"
```
- The first time, type `yes` to trust the server, then the **root password** (asked a few times).
- `-Support` is the contact shown inside every shop's app ("Contact: …").
- It takes 5–10 minutes and installs:
  - .NET
  - PostgreSQL (the database, with its own random password)
  - Caddy (free HTTPS that renews itself)
  - a firewall
  - the licence server as a service that restarts by itself
  - a nightly database backup
- Near the end it prints **"Licence public key: MFkwEwYH…"**. It must be the same text as `PublicKey` in
  `FreshMart.Pos/Services/LicenseService.cs`. If it isn't, the wrong key file was uploaded: stop and check.

The signing key is stored on the server in `/etc/yoeyar/` (only the service can read it). The uploaded copy is wiped.

## 4. Create your admin account

Open **https://license.yoeyar.com** straight away and create your admin account (long password).
The setup page closes once an admin exists.

## 5. Build the app for shops

1. In `FreshMart.Pos/Services/LicenseService.cs` set:
   ```csharp
   const string DefaultServer = "https://license.yoeyar.com";
   ```
2. Build with your Android keystore (see `INSTALL.md`):
   ```powershell
   .\build-release.ps1
   ```
3. Test once: install on your own phone, add a test shop in the admin panel, activate with its code.

## Updating the server later

After changing the server code (or pulling a new version from GitHub):
```powershell
.\deploy\publish-server.ps1 -Server <VPS IP>
```
Your shops, devices and admins stay; only the program is replaced.

## Backups

| What | Where | Your job |
|---|---|---|
| Database | Server makes one every night at 02:30 in `/var/backups/yoeyar/` (30 days kept) | Once a week, copy them to your PC: `.\deploy\get-backups.ps1 -Server <VPS IP>` → `server-backups\` (not uploaded to GitHub). Copy that folder to a USB stick or Google Drive too. |
| Signing key | `/etc/yoeyar/` on the server | Keep your two offline copies safe. It never changes. |
| Whole server | Z.com Auto Backup (if you ordered it) | Nothing |

**Restore the database** (for example on a new server, after step 3):
```powershell
scp .\server-backups\licenses-2026-11-03-0230.sql.gz root@<VPS IP>:/root/
ssh root@<VPS IP>
```
Then, on the server:
```bash
systemctl stop yoeyar-license
sudo -u postgres dropdb licenses && sudo -u postgres createdb -O yoeyar licenses
gunzip -c /root/licenses-2026-11-03-0230.sql.gz | sudo -u postgres psql -q licenses
systemctl start yoeyar-license
```

## Checking on the server

```powershell
ssh root@<VPS IP> yoeyar-server status
```
This shows whether it's running and the last log lines. Restart it with:
```powershell
ssh root@<VPS IP> systemctl restart yoeyar-license
```

## Safer sign-in (recommended after the first setup)

Typing the root password every time works, but an SSH key is safer and quicker:
```powershell
ssh-keygen -t ed25519            # press Enter for the defaults; choose a passphrase
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@<VPS IP> "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```
Afterwards `ssh root@<VPS IP>` no longer asks for the root password.

## If something goes wrong

| Problem | Fix |
|---|---|
| Browser says the site can't be reached / no HTTPS | The domain doesn't point to the VPS yet (step 2). Wait, then `ssh root@<VPS IP> systemctl restart caddy`. |
| "Licence signing key not found" in the status log | The key wasn't uploaded. Run step 3 again with `-KeyFile <path to your key backup>`. |
| "a DIFFERENT signing key is already installed" | Setup refused to replace the key on the server. Check which key the apps use (the `PublicKey` in `LicenseService.cs`) before changing anything. |
| Shops' apps say "Can't reach the licence server" | `yoeyar-server status` on the server; check the VPS is running in the Z.com panel. Shops keep selling for 30 days meanwhile. |
