using SQLite;

namespace FreshMart.Pos.Models;

public enum SellUnit { Each = 0, Kg = 1 }

/// How an item is sold on a sale line. Stock is always kept in pieces (or kg).
public enum PackType { Pcs = 0, Ctn = 1 }

public enum CreditTerm { Daily = 0, Weekly = 1, Monthly = 2 }

public class Product
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Unique, NotNull] public string Barcode { get; set; } = "";
    [NotNull] public string Name { get; set; } = "";       // Myanmar name (primary)
    public string NameEn { get; set; } = "";               // optional English name
    public string Category { get; set; } = "Other";
    public decimal Price { get; set; }                     // retail price per piece (or per kg)
    public decimal Cost { get; set; }                      // buying price per piece (or per kg); 0 = not entered
    public SellUnit Unit { get; set; }
    public int CtnQty { get; set; }                        // pieces per carton; 0 = not sold by carton
    public decimal CtnPrice { get; set; }                  // wholesale price per carton
    public decimal Stock { get; set; }                     // in pieces (or kg)
    public decimal ReorderLevel { get; set; }

    [Ignore] public bool HasCtn => Unit == SellUnit.Each && CtnQty > 1 && CtnPrice > 0;
    [Ignore] public bool IsLow => Stock <= ReorderLevel;
    [Ignore] public bool IsOut => Stock <= 0;

    public decimal PriceFor(PackType pack) => pack == PackType.Ctn && HasCtn ? CtnPrice : Price;
    public int UnitsFor(PackType pack) => pack == PackType.Ctn && HasCtn ? CtnQty : 1;
}

public class Customer
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [NotNull] public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public CreditTerm Term { get; set; } = CreditTerm.Weekly;
    public decimal CreditLimit { get; set; }               // 0 = no limit
    public bool IsWholesale { get; set; }                  // switches checkout to CTN mode
    public string Note { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public static int DueDays(CreditTerm t) => t switch { CreditTerm.Daily => 1, CreditTerm.Weekly => 7, _ => 30 };
}

public class Sale
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int ReceiptNo { get; set; }
    [Indexed] public DateTime CreatedAt { get; set; }
    [Indexed] public int CustomerId { get; set; }          // 0 = walk-in
    public string CustomerName { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal Discount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Method { get; set; } = "Cash";           // how the paid part was paid: Cash / Mobile / Credit (nothing paid)
    public decimal Tendered { get; set; }
    public decimal Change { get; set; }
    public decimal Paid { get; set; }                      // paid now (excludes change)
    public decimal Credit { get; set; }                    // Total - Paid, owed by the customer
    public DateTime? DueDate { get; set; }
    public decimal BalanceAfter { get; set; }              // customer's total debt right after this sale
    public bool Refunded { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string StaffName { get; set; } = "";
}

public class SaleLine
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int SaleId { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public SellUnit Unit { get; set; }
    public PackType Pack { get; set; }
    public int UnitsPerPack { get; set; } = 1;
    public decimal Price { get; set; }                     // per pack
    public decimal Cost { get; set; }                      // buying cost per pack at the time of sale; 0 = unknown
    public decimal Qty { get; set; }                       // in packs
    public decimal Amount { get; set; }

    [Ignore] public decimal BaseQty => Qty * Math.Max(1, UnitsPerPack);
    [Ignore] public decimal Profit => Amount - Cost * Qty;
}

public class CustomerPayment
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }                    // negative for debt paid back on a refund
    public string Method { get; set; } = "Cash";
    public string Note { get; set; } = "";
    public string StaffName { get; set; } = "";

    /// Method of a pay-back: cash returned for debt the customer had already paid on a refunded sale.
    public const string RefundMethod = "Refund";
}

public enum StaffRole { Cashier = 0, Owner = 1 }

public class Staff
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [NotNull] public string Name { get; set; } = "";
    public StaffRole Role { get; set; }
    public string PinHash { get; set; } = "";
    public string PinSalt { get; set; } = "";
    public bool Active { get; set; } = true;

    [Ignore] public bool IsOwner => Role == StaffRole.Owner;
}

/// End-of-day drawer count.
public class CashClose
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public DateTime Day { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string StaffName { get; set; } = "";
    public decimal OpeningFloat { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashCollected { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal Expected { get; set; }
    public decimal Counted { get; set; }
    public decimal Difference { get; set; }                // Counted - Expected: + over, - short
    public string Note { get; set; } = "";
}

/// Who did what — refunds, deletions, price changes, overrides.
public class AuditEntry
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public DateTime At { get; set; } = DateTime.Now;
    public string StaffName { get; set; } = "";
    public string Action { get; set; } = "";               // localisation key suffix: audit_<action>
    public string Detail { get; set; } = "";
}

public record CashExpected(decimal OpeningFloat, decimal CashSales, decimal CashCollected, decimal CashRefunds)
{
    public decimal Expected => OpeningFloat + CashSales + CashCollected - CashRefunds;
}

public class StoreSettings
{
    [PrimaryKey] public int Id { get; set; } = 1;
    public string Name { get; set; } = "Yoe Yar POS";      // replaced by the shop name from the licence
    public string Address { get; set; } = "Yangon";
    public string Phone { get; set; } = "";
    public string Currency { get; set; } = "Ks";
    public bool SymbolAfter { get; set; } = true;          // "1,500 Ks" vs "$1,500"
    public int Decimals { get; set; }                      // 0 for kyat
    public decimal TaxRate { get; set; }
    public string Footer { get; set; } = "ဝယ်ယူအားပေးမှုအတွက် ကျေးဇူးတင်ပါသည်";
    public string Language { get; set; } = "my";
    public int NextReceipt { get; set; } = 1001;
    public decimal OpeningFloat { get; set; }              // cash left in the drawer at the start of the day
    public decimal MaxCashierDiscount { get; set; } = 5;   // % a cashier may give without the owner's PIN
    public string PaymentInfo { get; set; } = "";          // e.g. "KPay 09 xxx (U Aung)" — shown in debt reminders
    public DateTime? LastExportAt { get; set; }            // last backup saved/shared off this device
}

public class CartLine
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public SellUnit Unit { get; set; }
    public PackType Pack { get; set; }
    public int UnitsPerPack { get; set; } = 1;
    public decimal Price { get; set; }                     // per pack
    public decimal Cost { get; set; }                      // per pack
    public decimal Qty { get; set; }                       // in packs
    public decimal Amount => PosMath.Money(Price * Qty);
    public decimal BaseQty => Qty * UnitsPerPack;
}

public record Totals(decimal Subtotal, decimal DiscountPct, decimal Discount, decimal Tax, decimal Total);

public record Backup(StoreSettings Settings, List<Product> Products, List<Sale> Sales, List<SaleLine> Lines,
                     List<Customer>? Customers = null, List<CustomerPayment>? Payments = null,
                     List<Staff>? Staff = null, List<CashClose>? CashCloses = null, List<AuditEntry>? Audit = null);

public record DebtSummary(Customer Customer, decimal Balance, DateTime? OldestDue, int OverdueDays, DateTime? LastPayment)
{
    public bool IsOverdue => OverdueDays > 0;
    public bool DueToday => OldestDue?.Date == DateTime.Today;
}

/// Refund of a credit sale: unpaid credit is cancelled, already-paid credit is handed back with the money paid at the sale.
public record RefundPlan(decimal CreditCancelled, decimal SettledReturned, decimal ReturnNow);

public record LedgerEntry(DateTime At, bool IsPayment, int RefId, string Ref, decimal Debt, decimal Payment, decimal Balance, bool Refunded);

public static class PosMath
{
    /// Currency decimals (0 for kyat). Set from settings at startup.
    public static int Decimals { get; set; }

    public static decimal Money(decimal v) => Math.Round(v, Decimals, MidpointRounding.AwayFromZero);
    public static decimal R3(decimal v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

    /// Line amounts are rounded first so the receipt always adds up.
    public static Totals Compute(IEnumerable<CartLine> lines, decimal discountPct, decimal taxRate)
    {
        var pct = Math.Clamp(discountPct, 0, 100);
        var sub = lines.Sum(l => l.Amount);
        var disc = Money(sub * pct / 100);
        var tax = Money((sub - disc) * taxRate / 100);
        return new Totals(sub, pct, disc, tax, sub - disc + tax);
    }
}
