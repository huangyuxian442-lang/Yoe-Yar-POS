# FreshMart POS — Business Analyst Test Report

**Date:** 02-11-2026 · **Build:** Release 3 (Windows) · **Tester role:** Senior BA
**Method:** Ran the real app with a separate test database (shop data untouched), as owner and as cashier,
drove each flow through the UI, then checked every number against the database. Native confirm dialogs and
printing were driven or reviewed by hand; business rules not reachable in one day (overdue ageing) were checked in code.

## Verdict
The core counter work is solid: selling PCS/CTN, part-credit, debt collection, profit, stock and cash count all
calculated correctly. **Three issues should be fixed before go-live** because they can lose money or data
without anyone noticing (H1–H3). The rest are improvements for the pilot.

## Fix status (02-11-2026)
| ID | Status | Retest result |
|---|---|---|
| H1 | ✅ Fixed | Same scenario: refund dialog now says return **36,000** (10,000 paid + 26,000 debt already paid). A "ငွေပြန်ပေး #1001 26,000" line is added to the statement; balance **0**. Cash count: 50,000 + 26,000 collected − 26,000 paid back = **50,000** expected. Any negative balance now shows as "ဆိုင်က ပေးရန်". |
| H2 | ✅ Fixed | Cashier's 105,000 credit on a 50,000 limit shows "🔒 Owner PIN needed"; sale only completes after owner PIN; "Over credit limit" entry in the activity log. |
| H3 | ✅ Fixed | Daily automatic backup on the device (newest 30, plus a safety copy before restore/reset) with Restore/Share in Settings → Data. Orange reminder bar until a backup leaves the device; "Back up now" needs owner PIN, saves the file, bar disappears, logged. |

## Defects and gaps

| ID | Severity | Area | Finding | Business impact | Recommendation |
|---|---|---|---|---|---|
| H1 | **High** | Refund + debt | Refunding a credit sale **after the customer already paid that debt** leaves a hidden **negative balance**. Test: #1002 = 36,000 (10,000 cash + 26,000 credit), customer paid 26,000, owner refunded → dialog says return only 10,000; customer balance becomes −26,000 but Customers/Debts screens show **0**. Deleting that customer would then fail with "has balance". | Customer either loses 26,000 Ks or gets paid twice; nobody can see the shop owes them. Cash count also expects 26,000 that may have been handed back. | On refund, split the sale into *unpaid credit* (just cancel) and *already settled* (cash to return, counted as a cash refund). Show any negative balance as **"ဆိုင်က ပေးရန်"** (shop owes customer) and let it offset the next purchase. |
| H2 | **High** | Credit control | A **cashier can sell on credit over the customer's limit** with only a yellow warning. Test: daily customer, limit 50,000, cashier sold 105,000 on credit. No owner PIN, nothing in the activity log. | Credit limits don't actually protect the shop; the biggest risk in a 50 %-credit business. | Over-limit credit needs the owner PIN (same pop-up as big discounts) and is written to the activity log. Optional: block credit while a customer is overdue. |
| H3 | **High** | Data safety | Backup is manual only. All debts live on one device. | Lost, broken or stolen phone = the whole debt book is gone. | Automatic daily backup (Android: to Google Drive / Downloads; Windows: second folder or USB) + a banner when the last backup is older than 1 day. |
| M1 | Medium | Returns | Only a **whole-receipt** refund. No way to return one item (broken egg tray, expired milk). | Cashiers will refund the whole sale and re-ring it — error-prone and messy in reports. | Partial return: choose lines/qty from the receipt. |
| M2 | Medium | Security | **No auto-lock.** If the owner signs in at the counter and walks away, anyone can refund, change prices, see profit. | Owner approvals can be bypassed in practice. | Auto-lock after X minutes idle (setting, default 5) and lock back to cashier after an owner action. |
| M3 | Medium | Cash count | Cashier sees the **expected amount before counting** and can save again. | Easy to "make it balance" — hides shortages. | Blind count for cashiers (show difference only after saving; expected visible to owner). One saved count per day, re-count needs owner. |
| M4 | Medium | Mobile money | Mobile payments record no **wallet** (KPay / Wave / AYA) and no **transaction ref**. The cash count covers cash only. | Can't match the day's mobile sales to the wallet statement; fake "I already transferred" goes unnoticed. | Keep it manual, but add wallet + last 4–6 digits of transaction ID, and a per-wallet total in the day close to tick off against the phone. |
| M5 | Medium | Pricing | Discount is **% only**. No fixed-amount / round-off (36,250 → 36,000) and no line price change for negotiated wholesale prices. | Cashiers will use odd % values or give change in goods; profit numbers drift. | Add "round off / amount discount" (cashier up to a small limit) and owner-approved line price override. |
| M6 | Medium | Stock | Stock can only be corrected by editing the product. No **reason** (damaged, expired, lost, stock-take). | Losses look like sales or disappear; can't see shrinkage. | Stock adjustment screen with reason codes and a simple stock-take (count → difference → adjust). |
| M7 | Medium | Checkout | **Held sale is lost** when the app restarts (verified). Only one sale can be on hold. | Android closes background apps; a waiting customer's basket must be re-scanned. | Save held sales to the database; allow several. |
| L1 | Low | Receipt | Cashier name not printed on receipt / slip (it is stored). | Harder to follow up disputes. | Print "ငွေကိုင်: name". |
| L2 | Low | Audit | Debt payments received are not in the activity log (stored on the payment). | Owner must look in two places. | Log them. |
| L3 | Low | Products | Deleting a product that has sales is a hard delete; a later refund silently skips returning stock. | Stock wrong after refund. | Deactivate instead of delete when there is history. |
| L4 | Low | Cash count | Opening float is one fixed setting, not per day / shift. | Wrong expected cash on days with a different float. | Ask for the float when the first sale of the day starts (default = setting). |
| L5 | Low | Navigation | After switching user, the new user lands on the previous user's page (e.g. locked Settings). | Minor confusion. | Go to Checkout after every login. |
| L6 | Low | Cosmetic | Window title "FreshMart.Pos"; data folder named "User Name". | Looks unfinished. | Set title/publisher (data folder change needs a one-time move). |

## Scenarios that passed

| # | Scenario | Expected | Result |
|---|---|---|---|
| 1 | Cashier login, owner-only pages | Settings locked, cost/profit hidden | ✅ |
| 2 | 3 × Coke PCS + 1 × Water CTN, cash 20,000 | 6,600 total, change 13,400 | ✅ |
| 3 | Sell 1 CTN (12) detergent with 5 pcs in stock | Blocked with message | ✅ "လက်ကျန် 5 ခု သာ" |
| 4 | Wholesale customer → auto CTN, 2 CTN Coke, pay 10,000 | Credit 26,000, due +30 days (02-12) | ✅ |
| 5 | Daily customer credit | Due next day (03-11) | ✅ |
| 6 | Collect 26,000 debt in cash | Slip, balance 0, "collected today" 26,000 | ✅ |
| 7 | Profit per line | Oil CTN 13,200 · Coke CTN 3,360 · Water CTN 120 · Coke PCS 360 | ✅ matches cost × pack |
| 8 | Owner refund of #1002 | Stock +48 pcs, audit entry | ✅ (but see H1) |
| 9 | Rice 2.5 kg scanned while in CTN mode | Weight prompt, 8,000 Ks, no carton | ✅ |
| 10 | Mobile payment | Not in expected cash | ✅ |
| 11 | Cash count: float 50,000 + cash sales 6,600 + collected 26,000 | Expected 82,600; counted 82,000 → short 600; logged | ✅ |
| 12 | Final stock after all sales/refund | Coke 397, Water 588, Oil 24, Rice 197.5 | ✅ |
| 13 | Restart app | Must sign in again | ✅ |

## Out of current scope (for the roadmap)
Supplier purchases and money the shop owes suppliers · expiry dates · multi-shop / central owner admin ·
customer price tiers · Bluetooth receipt printer on Android.

## Suggested order
1. H1, H2, H3 — before go-live.
2. M2, M3, M4 — before trusting staff alone at the counter.
3. M1, M5, M6, M7 — during the pilot, based on how often they come up.
