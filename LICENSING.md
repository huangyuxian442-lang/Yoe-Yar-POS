# Selling Yoe Yar POS — shops, plans and licences

## How it fits together

| Where | What is stored |
|---|---|
| **Each shop's device** (SQLite, as before) | Sales, debts, customers, stock, staff. Never sent to your server. |
| **Your licence server** (`FreshMart.LicenseServer`) | Shops, plans, limits, paid-until dates, activated devices, admin users, activity log. |

1. You add a shop in the admin panel. It gets a **shop code** like `YY-Z4WC-D7BN`.
2. On first start the app asks for the shop code (internet needed once). The server checks the device limit and
   sends back a **signed licence**. The app checks the signature with the server's public key, so the shop can't edit it.
3. The app works **offline**. It checks in on start and every 4 hours when there is internet, picking up plan changes.
   Without a check-in for **30 days** it stops new sales (reports, debts and backups still work) until it goes online.
4. The shop name on receipts comes from the licence, so a licence can't be reused for a differently named shop.

## Plans

| Plan | Devices | Cashier accounts (plus the owner) | Ends? |
|---|---|---|---|
| **Basic** | 1 | 1 | Never |
| **Standard** | 2 | 3 | Paid-until date |
| **Pro** | 5 | 10 | Paid-until date |
| **Custom** | you type them | you type them | Paid-until date |

Change the presets in `FreshMart.LicenseServer/Services/Plans.cs`.

**When a paid plan ends** the shop falls back to Basic instead of being locked out: the first device and the oldest cashier keep
working. Extra devices stop selling and extra cashiers can't sign in until you extend. The app shows an orange bar
from 7 days before the end. **Suspend** (for non-payment) stops selling on every device at its next check-in.

## Admin panel

| Page | What you do there |
|---|---|
| **Shops** | Totals, plus shops ending in 14 days, ended and suspended. Search by name, code, owner or phone. |
| **Shop** | Name, owner, phone, plan, paid-until date, notes. **+1 month / +1 year**, **Suspend / Resume**. Device list with **Remove** (lost or replaced phone, which frees the slot). |
| **Activity** | Every change: who, when, which shop. |
| **Admins** | Add people who can sign in, or reset a password. |

## Try it on this PC

```bash
cd FreshMart.LicenseServer
dotnet run --launch-profile http
```

1. Open http://localhost:5080. The first visit asks you to create the admin account.
2. Add a shop and copy its code.
3. Start the POS app on this PC and type the code.

The app talks to `http://localhost:5080` by default. Override it with the `FRESHMART_LICENSE_URL` environment variable.

## Before you sell — hosting checklist

**Using Z.com Cloud VPS (paid in kyat):** follow `deploy/ZCOM-GUIDE.md`. `deploy/publish-server.ps1` does steps 2 and 4
below for you (PostgreSQL, HTTPS, service, nightly backups). The same scripts work on any Ubuntu VPS.

1. **Back up `FreshMart.LicenseServer/App_Data/license-signing-key.pem`** (two offline copies). Every app trusts this key.
   If it's lost, you must ship a new app version to every shop. If it leaks, anyone can make licences.
   Never put it in a public repository.
2. **Host the server** with HTTPS on a domain, for example `https://license.yourbrand.com`:
   - **Managed (Azure App Service + Azure Database for PostgreSQL, Southeast Asia):** about USD 25–35 a month, with automatic database backups.
   - **VPS (DigitalOcean or Hetzner, Singapore):** about USD 6–12 a month. Run `dotnet publish` behind Caddy or nginx for HTTPS, and back up the database yourself.
   - Settings, in `appsettings.json` or environment variables:
     ```
     Database__Provider=postgres
     ConnectionStrings__Licenses=Host=…;Database=licenses;Username=…;Password=…
     License__SigningKeyPath=/secure/path/license-signing-key.pem   (copy the existing key there)
     Vendor__Support=Viber 09-xxx-xxx-xxx                            (shown in every shop's app)
     ```
   - SQLite also works on a VPS for a few hundred shops. Then back up `App_Data/licenses.db` daily.
3. **Point the app at it:** set `DefaultServer` in `FreshMart.Pos/Services/LicenseService.cs` to your `https://…` address,
   then rebuild the APK and Windows app. Android won't connect to plain `http://` addresses.
4. Create your admin account on the hosted server straight away. The setup page closes once one admin exists.
5. **Database changes:** the server creates its tables on first start. Before changing `Data/LicenseDb.cs` after go-live,
   switch to EF Core migrations so existing data is kept.

## Limits to know
- A shop that never connects again keeps selling for up to 30 days after its last check-in.
- Setting the device clock back is detected (more than 1 day behind the last time the app saw), and selling stops until the clock is fixed and the device goes online.
- Reinstalling the app on the same phone or PC keeps the same device ID, so it doesn't use another slot.
  A **factory-reset** Android phone gets a new ID. Remove the old entry in the admin panel.
