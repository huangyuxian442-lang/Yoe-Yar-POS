# FreshMart POS — Project Plan

## 1. Goal
Give a single-location grocery store a fast, offline-first checkout system that
replaces the manual cash register and paper stock book, and that a cashier can
learn in under 15 minutes.

## 2. Stakeholders
| Role | Needs |
|---|---|
| Owner | Daily sales totals, best sellers, low-stock warnings, refund control |
| Cashier | Fast scanning/search, weighed produce, correct change, receipts |
| Stock clerk | Add products, adjust quantities, see what to reorder |

## 3. Scope

### MVP (delivered in `pos.html`)
- **Checkout**: barcode scan / keyboard search, category grid, cart with qty edit,
  weighed items (price per kg), order discount, tax, cash/card/mobile payment,
  change calculation, printable receipt, hold & resume a sale.
- **Inventory**: add / edit / delete products, stock levels, low-stock threshold
  and highlighting, CSV export.
- **Sales**: today's revenue, transaction count, average basket, payment mix,
  top sellers, sale history with refund (restores stock), CSV export.
- **Settings**: store name, address, currency symbol, tax rate, data reset,
  JSON backup / restore.
- Works fully offline; data kept in the browser on the till PC.

### Release 2 (delivered in `FreshMart.Pos/`, .NET MAUI Blazor Hybrid)
- Myanmar (primary) + English interface, kyat formatting (`18,000 Ks`).
- Retail (PCS) and wholesale (CTN) prices per product on one barcode; colour-coded PCS/CTN
  checkout mode (F4), auto-CTN for wholesale customers, stock kept in pieces.
- Customer master with daily / weekly / monthly credit terms and credit limits.
- Credit sales with part payment ("half now, half credit"), debt collection screen with
  overdue / due-today ordering, payment slips, customer statements, cash-in-drawer report.

### Release 3
- Staff PIN login (owner / cashier), owner approval pop-up for sensitive actions, activity log.
- Cost price per product, profit & margin in reports.
- End-of-day cash count with note counter and shortage/overage history.
- Debt reminder messages for Viber / Messenger / SMS.

### Out of scope for MVP (Phase 2+)
- Multi-terminal sync / cloud backend
- Staff logins & role permissions
- Supplier purchase orders, expiry tracking
- Customer loyalty program
- Integrated card terminal (card is recorded, not processed)

## 4. Architecture
- **MVP**: single HTML file, vanilla JS, `localStorage` persistence. Zero install,
  runs in any modern browser, USB barcode scanners work out of the box
  (they act as keyboards and send Enter).
- **Phase 2 target**: Node/Express or Supabase backend + PostgreSQL, the same UI
  as a PWA with an offline queue that syncs when online.

## 5. Timeline (6 weeks)
| Week | Milestone |
|---|---|
| 1 | Requirements workshop with owner & cashiers, product list import |
| 2 | MVP checkout + inventory (this build) |
| 3 | In-store pilot on one till, shadow the paper process |
| 4 | Fix pilot feedback, receipt printer setup, staff training |
| 5 | Go-live, daily backup routine |
| 6 | Review metrics, plan Phase 2 |

## 6. Risks & mitigations
| Risk | Mitigation |
|---|---|
| Browser data cleared → lost sales | Daily JSON backup (Settings → Backup); Phase 2 server |
| Cashier error on weighed items | Weight prompt with live price preview |
| Power / PC failure | Paper fallback sheet; backup file on USB |
| Stock drift | Weekly stock count, adjust in Inventory |

## 7. Acceptance criteria
- Scan-to-cart under 1 second; a 10-item sale completed in under 60 seconds.
- Totals, tax and change are correct to the cent.
- Completed sale decrements stock; refund restores it.
- Low-stock items are flagged on the Inventory tab.
- Day's sales export to CSV opens correctly in Excel.

## 8. How to run
Open `pos.html` in Chrome or Edge. Shortcuts: **F2** search, **F9** pay,
**Esc** close dialogs. Print receipts with the browser print dialog
(set the receipt printer as default and paper width to 80 mm).
