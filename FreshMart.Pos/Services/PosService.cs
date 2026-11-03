using System.Text.Json;
using FreshMart.Pos.Data;
using FreshMart.Pos.Models;

namespace FreshMart.Pos.Services;

public class PosService(PosDatabase database, Loc L, AuthService Auth, LicenseService License)
{
    public StoreSettings Settings { get; private set; } = new();

    public async Task InitAsync()
    {
        var db = await database.GetAsync();
        Apply(await db.FindAsync<StoreSettings>(1) ?? new StoreSettings());
        await SyncShopNameAsync();
        await AutoBackupAsync();
    }

    /// The shop name comes from the licence, so one licence can't be used for a differently named shop.
    public async Task SyncShopNameAsync()
    {
        if (License.Info is not { ShopName.Length: > 0 } lic || Settings.Name == lic.ShopName) return;
        Settings.Name = lic.ShopName;
        await (await database.GetAsync()).InsertOrReplaceAsync(Settings);
    }

    void Apply(StoreSettings s)
    {
        Settings = s;
        Fmt.Configure(s);
        L.Set(s.Language);
    }

    // ---------- Settings ----------
    public async Task SaveSettingsAsync(StoreSettings s)
    {
        var db = await database.GetAsync();
        var current = await db.FindAsync<StoreSettings>(1) ?? new StoreSettings();
        s.Id = 1;
        s.NextReceipt = current.NextReceipt; // never let the form rewind receipt numbers
        s.LastExportAt = current.LastExportAt;
        if (License.Info is { ShopName.Length: > 0 } lic) s.Name = lic.ShopName;
        await db.InsertOrReplaceAsync(s);
        Apply(s);
        await Auth.LogAsync("settings", $"tax {Fmt.Num(s.TaxRate)}%, max discount {Fmt.Num(s.MaxCashierDiscount)}%, float {Fmt.Money(s.OpeningFloat)}");
    }

    public async Task SetLanguageAsync(string lang)
    {
        Settings.Language = lang;
        await (await database.GetAsync()).UpdateAsync(Settings);
        L.Set(lang);
    }

    // ---------- Products ----------
    public async Task<List<Product>> GetProductsAsync()
    {
        var db = await database.GetAsync();
        return await db.Table<Product>().OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();
    }

    /// Returns an error message, or null when saved.
    public async Task<string?> SaveProductAsync(Product p)
    {
        if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Barcode)) return L["err_name_barcode"];
        if (p.Price < 0 || p.CtnPrice < 0 || p.CtnQty < 0 || p.Cost < 0) return L["err_negative"];
        if (p.CtnPrice > 0 && p.CtnQty < 2) return L["err_ctn_qty"];
        p.Name = p.Name.Trim(); p.NameEn = (p.NameEn ?? "").Trim(); p.Barcode = p.Barcode.Trim();
        p.Category = string.IsNullOrWhiteSpace(p.Category) ? "Other" : p.Category.Trim();
        p.Price = PosMath.Money(p.Price);
        p.CtnPrice = PosMath.Money(p.CtnPrice);
        p.Cost = PosMath.Money(p.Cost);
        if (p.Unit == SellUnit.Kg) { p.CtnQty = 0; p.CtnPrice = 0; }
        p.Stock = PosMath.R3(p.Stock);

        var db = await database.GetAsync();
        var clash = await db.Table<Product>().FirstOrDefaultAsync(x => x.Barcode == p.Barcode && x.Id != p.Id);
        if (clash is not null) return L.F("err_barcode_used", clash.Name);

        var old = p.Id == 0 ? null : await db.FindAsync<Product>(p.Id);
        if (p.Id == 0) await db.InsertAsync(p); else await db.UpdateAsync(p);

        if (old is null) await Auth.LogAsync("product_add", $"{p.Name} {Fmt.Money(p.Price)}");
        else
        {
            var changes = new List<string>();
            if (old.Price != p.Price) changes.Add($"{L["retail_price"]} {Fmt.Money(old.Price)} → {Fmt.Money(p.Price)}");
            if (old.CtnPrice != p.CtnPrice) changes.Add($"{L["wholesale_price"]} {Fmt.Money(old.CtnPrice)} → {Fmt.Money(p.CtnPrice)}");
            if (old.Cost != p.Cost) changes.Add($"{L["cost_price"]} {Fmt.Money(old.Cost)} → {Fmt.Money(p.Cost)}");
            if (old.Stock != p.Stock) changes.Add($"{L["stock"]} {Fmt.Num(old.Stock)} → {Fmt.Num(p.Stock)}");
            if (changes.Count > 0) await Auth.LogAsync("product_edit", $"{p.Name}: {string.Join(", ", changes)}");
        }
        return null;
    }

    /// Adds received stock; a new cost (per piece) replaces the old one when given.
    public async Task AddStockAsync(int productId, decimal pieces, decimal? newCost = null)
    {
        var db = await database.GetAsync();
        var p = await db.FindAsync<Product>(productId);
        if (p is null) return;
        p.Stock = PosMath.R3(p.Stock + pieces);
        if (newCost is > 0) p.Cost = PosMath.Money(newCost.Value);
        await db.UpdateAsync(p);
        await Auth.LogAsync("stock_in", $"{p.Name} +{Fmt.Num(pieces)}{(newCost is > 0 ? " @ " + Fmt.Money(newCost.Value) : "")}");
    }

    public async Task DeleteProductAsync(Product p)
    {
        await (await database.GetAsync()).DeleteAsync<Product>(p.Id);
        await Auth.LogAsync("product_delete", p.Name);
    }

    // ---------- Customers ----------
    public async Task<List<Customer>> GetCustomersAsync(bool includeInactive = false)
    {
        var db = await database.GetAsync();
        var q = db.Table<Customer>();
        if (!includeInactive) q = q.Where(c => c.Active);
        return await q.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<string?> SaveCustomerAsync(Customer c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return L["err_customer_name"];
        if (c.CreditLimit < 0) return L["err_negative"];
        c.Name = c.Name.Trim(); c.Phone = (c.Phone ?? "").Trim(); c.Address = (c.Address ?? "").Trim(); c.Note = (c.Note ?? "").Trim();
        var db = await database.GetAsync();
        if (c.Id == 0) await db.InsertAsync(c); else await db.UpdateAsync(c);
        return null;
    }

    /// Customers with history are deactivated instead of deleted. Returns an error message or null.
    public async Task<string?> DeleteCustomerAsync(Customer c)
    {
        if (await GetBalanceAsync(c.Id) != 0) return L["err_customer_has_balance"];
        var db = await database.GetAsync();
        var hasHistory = await db.Table<Sale>().Where(s => s.CustomerId == c.Id).CountAsync() > 0
                      || await db.Table<CustomerPayment>().Where(p => p.CustomerId == c.Id).CountAsync() > 0;
        if (hasHistory) { c.Active = false; await db.UpdateAsync(c); }
        else await db.DeleteAsync(c);
        await Auth.LogAsync("customer_delete", c.Name);
        return null;
    }

    // ---------- Credit / debt ----------
    /// True when new credit would take the customer above their credit limit (0 = no limit).
    public static bool IsOverLimit(Customer? c, decimal balanceBefore, decimal newCredit) =>
        c is { CreditLimit: > 0 } && newCredit > 0 && balanceBefore + newCredit > c.CreditLimit;

    public async Task<decimal> GetBalanceAsync(int customerId)
    {
        if (customerId == 0) return 0;
        var db = await database.GetAsync();
        var debt = await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Credit),0) FROM Sale WHERE CustomerId = ? AND Refunded = 0", customerId);
        var paid = await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Amount),0) FROM CustomerPayment WHERE CustomerId = ?", customerId);
        return debt - paid;
    }

    /// Balance per customer, with the due date of the oldest unpaid credit sale (payments settle oldest debts first).
    public async Task<List<DebtSummary>> GetDebtsAsync()
    {
        var db = await database.GetAsync();
        var customers = await db.Table<Customer>().ToListAsync();
        var credits = await db.Table<Sale>().Where(s => s.CustomerId > 0 && s.Credit > 0 && !s.Refunded).OrderBy(s => s.CreatedAt).ToListAsync();
        var payments = await db.Table<CustomerPayment>().ToListAsync();

        var result = new List<DebtSummary>();
        foreach (var c in customers)
        {
            var mine = credits.Where(s => s.CustomerId == c.Id).ToList();
            var myPays = payments.Where(p => p.CustomerId == c.Id).ToList();
            if (!c.Active && mine.Count == 0) continue;
            var paid = myPays.Sum(p => p.Amount);
            var balance = mine.Sum(s => s.Credit) - paid;

            DateTime? oldestDue = null;
            var left = paid;
            foreach (var s in mine)
            {
                if (left >= s.Credit) { left -= s.Credit; continue; }
                oldestDue = s.DueDate ?? s.CreatedAt.Date.AddDays(Customer.DueDays(c.Term));
                break;
            }
            var overdue = balance > 0 && oldestDue is { } d && d.Date < DateTime.Today ? (DateTime.Today - d.Date).Days : 0;
            var received = myPays.Where(p => p.Amount > 0).ToList();
            result.Add(new DebtSummary(c, balance, balance > 0 ? oldestDue : null, overdue, received.Count > 0 ? received.Max(p => p.CreatedAt) : null));
        }
        return result;
    }

    public async Task<List<LedgerEntry>> GetLedgerAsync(int customerId)
    {
        var db = await database.GetAsync();
        var sales = await db.Table<Sale>().Where(s => s.CustomerId == customerId && s.Credit > 0).ToListAsync();
        var pays = await db.Table<CustomerPayment>().Where(p => p.CustomerId == customerId).ToListAsync();
        var rows = sales.Select(s => (s.CreatedAt, false, s.Id, "#" + s.ReceiptNo, s.Refunded ? 0 : s.Credit, 0m, s.Refunded))
            // a refund pay-back is money going to the customer, so it shows in the debt column
            .Concat(pays.Select(p => (p.CreatedAt, true, p.Id, L.Method(p.Method) + (p.Note.Length > 0 ? " · " + p.Note : ""),
                Math.Max(0, -p.Amount), Math.Max(0, p.Amount), false)))
            .OrderBy(r => r.Item1);
        var bal = 0m;
        return rows.Select(r => { bal += r.Item5 - r.Item6; return new LedgerEntry(r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, bal, r.Item7); }).ToList();
    }

    /// Records money received against a customer's debt. Returns an error message or null.
    public async Task<string?> ReceivePaymentAsync(int customerId, decimal amount, string method, string note)
    {
        amount = PosMath.Money(amount);
        if (amount <= 0) return L["err_amount"];
        var balance = await GetBalanceAsync(customerId);
        if (amount > balance) return L.F("err_more_than_balance", Fmt.Money(balance));
        await (await database.GetAsync()).InsertAsync(new CustomerPayment
        {
            CustomerId = customerId, Amount = amount, Method = method, Note = note?.Trim() ?? "", CreatedAt = DateTime.Now,
            StaffName = Auth.CurrentName,
        });
        await AutoBackupAsync();
        return null;
    }

    public async Task DeletePaymentAsync(int paymentId, string customerName)
    {
        var db = await database.GetAsync();
        var p = await db.FindAsync<CustomerPayment>(paymentId);
        if (p is null) return;
        await db.DeleteAsync(p);
        await Auth.LogAsync("payment_delete", $"{customerName} {Fmt.Money(p.Amount)} ({p.CreatedAt:dd-MM-yyyy})");
    }

    /// Debt payments received from the start of <paramref name="first"/> to the end of <paramref name="last"/> (not refund pay-backs).
    public async Task<List<CustomerPayment>> GetPaymentsAsync(DateTime first, DateTime last)
    {
        var db = await database.GetAsync();
        var from = first.Date; var to = last.Date.AddDays(1);
        return await db.Table<CustomerPayment>().Where(p => p.CreatedAt >= from && p.CreatedAt < to && p.Amount > 0).ToListAsync();
    }

    public async Task<decimal> GetCollectedAsync(DateTime day) => (await GetPaymentsAsync(day, day)).Sum(p => p.Amount);

    // ---------- Sales ----------
    /// <param name="amount">Cash handed over (may exceed the total), or the amount paid by mobile. The rest becomes credit.</param>
    public async Task<(Sale Sale, List<SaleLine> Lines)> CheckoutAsync(CartService cart, string method, decimal amount)
    {
        var db = await database.GetAsync();
        var t = PosMath.Compute(cart.Lines, cart.DiscountPct, Settings.TaxRate);
        amount = Math.Max(0, PosMath.Money(amount));
        var paid = Math.Min(amount, t.Total);
        var credit = t.Total - paid;
        var customer = cart.Customer;
        if (credit > 0 && customer is null) throw new InvalidOperationException(L["err_credit_needs_customer"]);

        var now = DateTime.Now;
        var sale = new Sale
        {
            CreatedAt = now,
            CustomerId = customer?.Id ?? 0, CustomerName = customer?.Name ?? "",
            Subtotal = t.Subtotal, DiscountPct = t.DiscountPct, Discount = t.Discount,
            TaxRate = Settings.TaxRate, Tax = t.Tax, Total = t.Total,
            Method = paid == 0 && credit > 0 ? "Credit" : method,
            Tendered = method == "Cash" ? amount : paid,
            Change = method == "Cash" ? Math.Max(0, amount - t.Total) : 0,
            Paid = paid, Credit = credit,
            DueDate = credit > 0 ? now.Date.AddDays(Customer.DueDays(customer!.Term)) : null,
            BalanceAfter = customer is null ? 0 : await GetBalanceAsync(customer.Id) + credit,
            StaffName = Auth.CurrentName,
        };
        var lines = new List<SaleLine>();

        await db.RunInTransactionAsync(conn =>
        {
            foreach (var g in cart.Lines.GroupBy(l => l.ProductId))
            {
                var p = conn.Find<Product>(g.Key) ?? throw new InvalidOperationException(L.F("err_product_gone", g.First().Name));
                if (p.Stock < g.Sum(l => l.BaseQty)) throw new InvalidOperationException(L.F("only_in_stock", Fmt.Stock(p, L), L.Name(p)));
            }
            var settings = conn.Find<StoreSettings>(1);
            sale.ReceiptNo = settings.NextReceipt++;
            conn.Update(settings);
            conn.Insert(sale);
            foreach (var l in cart.Lines)
            {
                var p = conn.Find<Product>(l.ProductId);
                var line = new SaleLine
                {
                    SaleId = sale.Id, ProductId = l.ProductId, Name = l.Name, NameEn = l.NameEn, Unit = l.Unit,
                    Pack = l.Pack, UnitsPerPack = l.UnitsPerPack, Price = l.Price, Qty = l.Qty, Amount = l.Amount,
                    Cost = p.Cost * l.UnitsPerPack,   // cost at the moment of sale, per pack
                };
                conn.Insert(line);
                lines.Add(line);
                p.Stock = PosMath.R3(p.Stock - l.BaseQty);
                conn.Update(p);
            }
        });

        if (sale.DiscountPct > Settings.MaxCashierDiscount)
            await Auth.LogAsync("discount", $"#{sale.ReceiptNo} {Fmt.Num(sale.DiscountPct)}% = {Fmt.Money(sale.Discount)}");
        if (IsOverLimit(customer, sale.BalanceAfter - credit, credit))
            await Auth.LogAsync("over_limit", $"#{sale.ReceiptNo} {customer!.Name}: {Fmt.Money(sale.BalanceAfter)} / {Fmt.Money(customer.CreditLimit)}");
        Settings.NextReceipt = sale.ReceiptNo + 1;
        cart.Clear();
        await AutoBackupAsync();
        return (sale, lines);
    }

    /// What refunding a sale means for the money. Credit the customer has not paid yet is simply cancelled;
    /// credit they already paid off (beyond what they still owe overall) is handed back with the money paid at the sale.
    public async Task<RefundPlan> GetRefundPlanAsync(Sale s)
    {
        if (s.Credit <= 0 || s.CustomerId == 0) return new(0, 0, s.Paid);
        var balance = await GetBalanceAsync(s.CustomerId);
        var settled = Math.Clamp(s.Credit - balance, 0, s.Credit);
        return new(s.Credit - settled, settled, s.Paid + settled);
    }

    /// Refunds a whole sale and returns how much money goes back to the customer.
    public async Task<RefundPlan?> RefundAsync(int saleId)
    {
        var db = await database.GetAsync();
        var current = await db.FindAsync<Sale>(saleId);
        if (current is null || current.Refunded) return null;
        var plan = await GetRefundPlanAsync(current);
        Sale? refunded = null;
        await db.RunInTransactionAsync(conn =>
        {
            var sale = conn.Find<Sale>(saleId);
            if (sale is null || sale.Refunded) return;
            sale.Refunded = true;
            sale.RefundedAt = DateTime.Now;
            conn.Update(sale);
            // debt payments already made for this sale are paid back in cash, so the customer's balance stays correct
            if (plan.SettledReturned > 0)
                conn.Insert(new CustomerPayment
                {
                    CustomerId = sale.CustomerId, Amount = -plan.SettledReturned, Method = CustomerPayment.RefundMethod,
                    Note = "#" + sale.ReceiptNo, CreatedAt = sale.RefundedAt.Value, StaffName = Auth.CurrentName,
                });
            foreach (var l in conn.Table<SaleLine>().Where(x => x.SaleId == saleId).ToList())
            {
                var p = conn.Find<Product>(l.ProductId);
                if (p is null) continue;
                p.Stock = PosMath.R3(p.Stock + l.BaseQty);
                conn.Update(p);
            }
            refunded = sale;
        });
        if (refunded is null) return null;
        await Auth.LogAsync("refund", $"#{refunded.ReceiptNo} {Fmt.Money(refunded.Total)}{(refunded.CustomerName.Length > 0 ? " · " + refunded.CustomerName : "")}"
            + $" · {L["money_to_return"]} {Fmt.Money(plan.ReturnNow)}");
        return plan;
    }

    /// Sales from the start of <paramref name="first"/> to the end of <paramref name="last"/>, newest first.
    public async Task<List<Sale>> GetSalesAsync(DateTime first, DateTime last)
    {
        var db = await database.GetAsync();
        var from = first.Date; var to = last.Date.AddDays(1);
        return await db.Table<Sale>().Where(s => s.CreatedAt >= from && s.CreatedAt < to).OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<List<SaleLine>> GetLinesAsync(IEnumerable<int> saleIds)
    {
        var ids = saleIds.ToList();
        if (ids.Count == 0) return [];
        var db = await database.GetAsync();
        return await db.Table<SaleLine>().Where(l => ids.Contains(l.SaleId)).ToListAsync();
    }

    // ---------- End-of-day cash count ----------
    /// What should be in the drawer for a day: float + cash taken at sales + cash debt payments − cash given back for refunds.
    public async Task<CashExpected> GetCashExpectedAsync(DateTime day, decimal openingFloat)
    {
        var db = await database.GetAsync();
        var from = day.Date; var to = from.AddDays(1);
        var sales = await db.Table<Sale>().Where(s => s.CreatedAt >= from && s.CreatedAt < to && !s.Refunded && s.Method == "Cash").ToListAsync();
        // sales from earlier days refunded today: that cash leaves the drawer today
        var refunds = await db.Table<Sale>().Where(s => s.Refunded && s.RefundedAt >= from && s.RefundedAt < to && s.CreatedAt < from && s.Method == "Cash").ToListAsync();
        var pays = await db.Table<CustomerPayment>().Where(p => p.CreatedAt >= from && p.CreatedAt < to
            && (p.Method == "Cash" || p.Method == CustomerPayment.RefundMethod)).ToListAsync();
        // paid-off debt handed back on a refund also leaves the drawer
        var paidBack = -pays.Where(p => p.Amount < 0).Sum(p => p.Amount);
        return new CashExpected(openingFloat, sales.Sum(s => s.Paid), pays.Where(p => p.Amount > 0).Sum(p => p.Amount), refunds.Sum(s => s.Paid) + paidBack);
    }

    public async Task<CashClose> SaveCashCloseAsync(DateTime day, CashExpected e, decimal counted, string note)
    {
        var c = new CashClose
        {
            Day = day.Date, CreatedAt = DateTime.Now, StaffName = Auth.CurrentName,
            OpeningFloat = e.OpeningFloat, CashSales = e.CashSales, CashCollected = e.CashCollected, CashRefunds = e.CashRefunds,
            Expected = e.Expected, Counted = PosMath.Money(counted), Difference = PosMath.Money(counted) - e.Expected, Note = note?.Trim() ?? "",
        };
        await (await database.GetAsync()).InsertAsync(c);
        await Auth.LogAsync("cash_close", $"{day:dd-MM-yyyy} {L["expected"]} {Fmt.Money(c.Expected)}, {L["counted"]} {Fmt.Money(c.Counted)}, {L["difference"]} {Fmt.Money(c.Difference)}");
        return c;
    }

    public async Task<List<CashClose>> GetCashClosesAsync(DateTime first, DateTime last)
    {
        var db = await database.GetAsync();
        var from = first.Date; var to = last.Date;
        return await db.Table<CashClose>().Where(c => c.Day >= from && c.Day <= to).OrderByDescending(c => c.CreatedAt).ToListAsync();
    }

    // ---------- Backup ----------
    public async Task<Backup> CreateBackupAsync()
    {
        var db = await database.GetAsync();
        return new Backup(
            await db.FindAsync<StoreSettings>(1),
            await db.Table<Product>().ToListAsync(),
            await db.Table<Sale>().ToListAsync(),
            await db.Table<SaleLine>().ToListAsync(),
            await db.Table<Customer>().ToListAsync(),
            await db.Table<CustomerPayment>().ToListAsync(),
            await db.Table<Staff>().ToListAsync(),
            await db.Table<CashClose>().ToListAsync(),
            await db.Table<AuditEntry>().ToListAsync());
    }

    static readonly JsonSerializerOptions BackupJson = new() { WriteIndented = true };

    public async Task<string> CreateBackupJsonAsync() => JsonSerializer.Serialize(await CreateBackupAsync(), BackupJson);

    /// Reads a backup file; throws <see cref="JsonException"/> when it isn't one.
    public static Backup ParseBackup(string json)
    {
        var b = JsonSerializer.Deserialize<Backup>(json);
        if (b?.Settings is null || b.Products is null || b.Sales is null || b.Lines is null) throw new JsonException();
        return b;
    }

    /// Remembers that a backup was saved or shared off this device (the reminder banner goes away).
    public async Task MarkExportedAsync()
    {
        Settings.LastExportAt = DateTime.Now;
        await (await database.GetAsync()).UpdateAsync(Settings);
        await Auth.LogAsync("backup", "");
    }

    /// Days since the last backup that left this device, or null when there never was one.
    public int? DaysSinceExport => Settings.LastExportAt is { } d ? (DateTime.Today - d.Date).Days : null;

    // ---------- automatic backups on this device ----------
    public static string AutoBackupDir => Path.Combine(Path.GetDirectoryName(PosDatabase.DbPath)!, "backups");
    const int KeepAutoBackups = 30;
    DateTime _lastAuto;

    /// Writes today's automatic backup (refreshed at most once an hour) and keeps the newest 30 days.
    /// <paramref name="tag"/> makes a separate file, e.g. a safety copy before a restore.
    public async Task AutoBackupAsync(string? tag = null)
    {
        if (tag is null && DateTime.Now - _lastAuto < TimeSpan.FromHours(1)) return;
        try
        {
            Directory.CreateDirectory(AutoBackupDir);
            var name = tag is null ? $"auto-{DateTime.Today:yyyy-MM-dd}.json" : $"auto-{DateTime.Now:yyyy-MM-dd-HHmm}-{tag}.json";
            var path = Path.Combine(AutoBackupDir, name);
            await File.WriteAllTextAsync(path + ".tmp", await CreateBackupJsonAsync());
            File.Move(path + ".tmp", path, overwrite: true);   // never leave a half-written backup
            if (tag is null) _lastAuto = DateTime.Now;
            foreach (var old in GetAutoBackups().Skip(KeepAutoBackups)) old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a failed backup must never stop a sale
        }
    }

    /// Automatic backups, newest first.
    public static List<FileInfo> GetAutoBackups() =>
        Directory.Exists(AutoBackupDir)
            ? new DirectoryInfo(AutoBackupDir).GetFiles("auto-*.json").OrderByDescending(f => f.LastWriteTime).ToList()
            : [];

    /// Replaces everything with the backup. Staff logins are only replaced when the backup contains staff.
    public async Task RestoreAsync(Backup b)
    {
        await AutoBackupAsync("before-restore");
        var db = await database.GetAsync();
        await db.RunInTransactionAsync(conn =>
        {
            DeleteAll(conn);
            conn.Insert(b.Settings);
            conn.InsertAll(b.Products, runInTransaction: false);
            conn.InsertAll(b.Sales, runInTransaction: false);
            conn.InsertAll(b.Lines, runInTransaction: false);
            conn.InsertAll(b.Customers ?? [], runInTransaction: false);
            conn.InsertAll(b.Payments ?? [], runInTransaction: false);
            conn.InsertAll(b.CashCloses ?? [], runInTransaction: false);
            if (b.Staff is { Count: > 0 }) { conn.DeleteAll<Staff>(); conn.InsertAll(b.Staff, runInTransaction: false); }
        });
        await InitAsync();
        await Auth.LogAsync("restore", $"{b.Products.Count} products, {b.Sales.Count} sales");
    }

    /// Clears all shop data and loads demo data. Staff logins and the audit log are kept.
    public async Task ResetToDemoAsync()
    {
        await AutoBackupAsync("before-reset");
        var db = await database.GetAsync();
        await db.RunInTransactionAsync(conn =>
        {
            DeleteAll(conn);
            conn.Insert(new StoreSettings());
            conn.InsertAll(PosDatabase.DemoProducts(), runInTransaction: false);
            conn.InsertAll(PosDatabase.DemoCustomers(), runInTransaction: false);
        });
        await InitAsync();
        await Auth.LogAsync("reset", "");
    }

    static void DeleteAll(SQLite.SQLiteConnection conn)
    {
        conn.DeleteAll<CustomerPayment>(); conn.DeleteAll<Customer>(); conn.DeleteAll<CashClose>();
        conn.DeleteAll<SaleLine>(); conn.DeleteAll<Sale>(); conn.DeleteAll<Product>(); conn.DeleteAll<StoreSettings>();
    }
}
