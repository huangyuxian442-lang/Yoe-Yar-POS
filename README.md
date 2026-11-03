# Yoe Yar POS

Point-of-sale app for a local grocery store (retail + wholesale, cash + credit), built with
**.NET MAUI Blazor Hybrid** (one C# codebase → Windows, Android, iOS, macOS) and a local **SQLite** database.
The interface is in **Myanmar (default) and English** — switch with the မြန်မာ / EN buttons at the top.

| Folder | What's in it |
|---|---|
| `FreshMart.Pos/` | The MAUI app |
| `FreshMart.LicenseServer/` | Your admin panel and licence server: shops, plans, device and cashier limits (see `LICENSING.md`) |
| `FreshMart.Licensing/` | Signed-licence format shared by the app and the server |
| `INSTALL.md`, `build-release.ps1`, `installer/` | Building release files and installing on clients' devices |
| `pos.html` | Original single-file browser prototype (kept for reference) |
| `PROJECT_PLAN.md` | Scope, timeline, risks, acceptance criteria |

## Features

### Checkout (အရောင်း)
- Barcode scanner / search, category grid, items sold by weight (kg), % discount, optional tax, hold & resume.
- **Retail and wholesale on the same barcode.** A big switch at the top picks how scans are added:
  - **ခု (PCS) · လက်လီ** — green. Each scan adds one piece at the retail price.
  - **ပုံး (CTN) · လက်ကား** — blue. Each scan adds one carton at the carton price. The search box turns blue too.
  - **F4** toggles the mode. Choosing a *wholesale customer* switches to CTN automatically.
  - In the cart, PCS lines have a green edge and CTN lines are blue with "1 ပုံး = 24 ခု". Tap the
    ခု ⇄ / ပုံး ⇄ badge on a line to switch it if the cashier scanned in the wrong mode.
  - Product tiles show both prices; the one for the current mode is highlighted.
- **Customer** — tap the customer box at the top of the cart (search by name or phone, or add a new customer on the spot).
- **Payment** — ငွေသား Cash, မိုဘိုင်း KPay/Wave, or အကြွေး Credit. Type what the customer pays now
  (quick buttons: အပြည့် full, တစ်ဝက် half, bank-note amounts). **Whatever isn't paid becomes credit**
  on the customer's account — so "half now, half credit" is just tap **တစ်ဝက်** and complete.
  Credit needs a customer; a warning shows when the customer goes over their credit limit.
- Receipt shows paid amount, this sale's credit, previous debt, total debt and the due date.

### Debts (အကြွေး) — collecting money
- One list of everyone who owes money, **overdue first** (red edge), then due today (orange), then by amount.
- Summary cards: total outstanding, overdue, due today, collected today.
- Filters: all / overdue / daily / weekly / monthly, plus search by name or phone.
- **ငွေလက်ခံမည် (Receive payment)** on each row → enter amount (or tap အားလုံးဆပ် pay-all / တစ်ဝက် half),
  choose cash or mobile, optional note → confirm → printable payment slip.
- Payments settle the **oldest** credit first; the due date shown is that of the oldest unpaid sale.
- **အကြွေးစာရင်း (Statement)** shows every credit sale and payment with a running balance; it can be printed,
  and a payment entered by mistake can be deleted there.

### Customers (ဖောက်သည်)
Name, phone, address, credit term (**နေ့စဉ် daily / အပတ်စဉ် weekly / လစဉ် monthly** — due 1 / 7 / 30 days after the sale),
credit limit, wholesale flag, note. Customers with history are deactivated rather than deleted.

### Inventory (ပစ္စည်းစာရင်း)
Myanmar + optional English name, barcode, category, **retail price per piece**, **pieces per carton** and **carton price**.
Stock is kept in pieces and shown as "15 ပုံး + 14 ခု". **ပစ္စည်းဝင် (Stock in)** adds stock in cartons and/or pieces.
Low-stock flags and CSV export.

### Sales report (နေ့စဉ်စာရင်း)
Pick a period with one tap — **ယနေ့ Today · မနေ့က Yesterday · ဤအပတ် This week · ဤလ This month · ပြီးခဲ့သောလ Last month** —
or set any **From / To** dates. Multi-day periods add a **day-by-day table with a total row** and an
"Export daily summary" CSV (open in Excel for the monthly report).
For the period: total sales, money received at sale, credit given, debt collected, **cash in drawer** (cash sales + cash debt
payments), top sellers by pack type, payment mix, transactions with refund and receipt reprint. CSV export.

### Staff login & owner approval (ဝန်ထမ်း)
- **First start:** create the **owner** account (name + 4–8 digit PIN). Don't forget this PIN.
- Each person signs in with their own PIN; the 🔒 button at the top switches user (the current sale stays in the cart).
- **Cashier (ငွေကိုင်)** can: sell, add customers, receive debt payments, stock in, do the cash count.
- **Owner PIN needed (ပိုင်ရှင်):** refunds, add/edit/delete products and prices, delete customers or debt payments,
  discounts above the limit (Settings, default 5%), Settings, cost prices and profit.
  When a cashier tries one of these, a PIN box pops up — the owner types their PIN on the spot and the action continues.
- **Activity log (Settings → လုပ်ဆောင်မှု မှတ်တမ်း):** who did what and when — sign-ins, refunds, owner approvals,
  big discounts, price/cost changes, deleted payments, stock in, cash counts.

### Cost price & profit (ဝယ်ဈေး / အမြတ်)
- Each product has a **cost per piece**; the inventory shows the margin for both PCS and CTN (red if selling below cost).
- **Stock in** can update the cost (enter cost per carton — it's converted to per piece).
- The cost is saved with every sale, so later price changes don't rewrite old profit.
- **Sales report** (owner only): profit and margin %, profit per day in the day-by-day table, profit per product.
  Lines sold without a cost price are counted as full profit and flagged ("ဝယ်ဈေးမရှိသော ပစ္စည်း N ကြောင်း").

### End-of-day cash count (ငွေစာရင်းပိတ်)
Sales report → **💵 ငွေစာရင်းပိတ်**. Shows what *should* be in the drawer:
opening float + cash sales + cash debt payments − cash refunds. Count the notes (10,000 / 5,000 / 1,000 / 500 / 200 / 100 / 50)
or type the total; the app shows **ကိုက်ညီ balanced / လို short / ပို over**. Every count is kept in a history table with who counted.

### Debt reminder (သတိပေးစာ)
Debts page → **📤 သတိပေးစာ** builds a ready-to-send message: balance, due date or days overdue, unpaid receipts and
your KPay/Wave details (Settings → payment details). Edit it, then **Copy** and paste into Viber / Messenger,
or **Share** (on Android this opens Viber, Messenger, SMS…).

### Refunds of credit sales
Refunding a sale cancels the credit the customer has not paid yet. If they already paid off some of that credit,
the app adds it to **"money to return"** (ဖောက်သည်ကို ပြန်ပေးရမည့်ငွေ) and records a **ငွေပြန်ပေး** line in their statement,
so their balance stays right and the cash count expects that cash to leave the drawer.
A customer the shop owes money to shows **ဆိုင်က ပေးရန်** (shop owes) in blue; it is offset by their next credit purchase.

### Credit limit
Credit that takes a customer over their limit needs the **owner's PIN** (cashiers see "🔒 Owner PIN needed")
and is written to the activity log. A limit of 0 means no limit.

### Backups
- **Automatic:** the app saves a backup on this device every day (refreshed hourly while in use, newest 30 kept),
  and always before a restore or demo reset. Settings → Data lists them with **Restore** and **Share**.
- **Off-device:** an orange bar appears when no backup has left the device for 2 days.
  **ယခု Backup လုပ်မည်** (owner PIN) saves the file — on Android the share sheet opens: send it to Google Drive or to yourself on Viber/Telegram.
  On Windows it goes to `Documents\Yoe Yar POS\`; copy it to a USB stick or a OneDrive/Google Drive folder.

### Licence (လိုင်စင်)
On first start the app asks for the **shop code** from the seller (internet needed once). The plan sets how many devices
and cashier accounts the shop may use (Basic: 1 device, owner + 1 cashier). The shop name comes from the licence.
Settings → လိုင်စင် shows the plan, limits, paid-until date and **Check now**. Details and hosting: `LICENSING.md`.

### Settings (ဆက်တင်)
Store name, address, phone, language, currency (default **Ks**, no decimals), tax, receipt footer, backup / restore, demo reset.

**Shortcuts:** F2 search · F4 PCS/CTN · F9 pay · Esc close.

## Editing the Myanmar / English text
All wording is in [`FreshMart.Pos/Localization/Strings.cs`](FreshMart.Pos/Localization/Strings.cs) —
each line has the Myanmar text first, then English. Change the text, rebuild, done.

## Project layout
```
FreshMart.Pos/
├─ Models/Models.cs          Product (PCS + CTN prices), Customer, Sale (paid/credit), CustomerPayment, PosMath
├─ Data/PosDatabase.cs       SQLite setup, upgrades older databases, demo products & customers
├─ Localization/Strings.cs   All UI text in Myanmar and English
├─ Services/
│  ├─ CartService.cs         Current sale: PCS/CTN mode, customer, stock checks, hold/resume
│  ├─ PosService.cs          Products, customers, checkout, credit balances, payments, reports, backup
│  ├─ Loc.cs                 Language switching
│  └─ UiServices.cs          Money formatting (Ks), CSV, toasts, confirm dialogs, file export
├─ Components/Pages/         Checkout, Debts, Customers, Inventory, Sales, Settings
├─ Components/Shared/        Modal, Receipt, CustomerPicker
└─ wwwroot/                  app.css, js/pos.js (focus, print, hotkeys)
```

## Run
Requires the .NET 10 SDK with MAUI workloads (installed on this PC).

```bash
cd FreshMart.Pos
dotnet build -t:Run -f net10.0-windows10.0.19041.0
```
Android (device or emulator connected): `dotnet build -t:Run -f net10.0-android`. iOS / macOS need a Mac.

### Android APK
```bash
dotnet build -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```
For the files you give to shops (APK + Windows installer zip) run `.\build-release.ps1`; see `INSTALL.md`.
Copy it to an Android phone/tablet (Android 7.0+), allow "install unknown apps", and open it.
This APK is signed with the development key — fine for installing on the shop's own devices;
publishing to Google Play needs a proper release keystore.

## Where data lives
- Database: `%LOCALAPPDATA%\Yoe Yar\com.yoeyar.pos\Data\freshmart-pos.db3` on Windows (data from the older FreshMart folder is copied there on first start).
  Older databases are upgraded automatically on first start. Automatic backups are in the `backups` folder next to it.
- Testing: set the environment variable `FRESHMART_DB` to another file path to run the app on a separate database.
- CSV exports and backups: `Documents\Yoe Yar POS\` on Windows/Mac; the share sheet on Android/iOS.

## Known limits (next steps)
- Receipt printing uses the system print dialog — fine on Windows/Mac; Android/iOS need a Bluetooth ESC-POS printer integration.
- Mobile-money payments are recorded, not processed (no KPay/Wave API).
- Single till — multi-till / multi-shop sync is Phase 2 (see `PROJECT_PLAN.md`).
- Open items from the BA test: see `TEST_REPORT.md` (M1–M7, L1–L6).
