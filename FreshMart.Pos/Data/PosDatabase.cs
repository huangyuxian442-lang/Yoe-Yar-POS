using FreshMart.Pos.Models;
using SQLite;

namespace FreshMart.Pos.Data;

public class PosDatabase
{
    // FRESHMART_DB lets testers run against a separate database without touching the shop's data
    public static string DbPath => Environment.GetEnvironmentVariable("FRESHMART_DB") is { Length: > 0 } path
        ? path
        : Path.Combine(FileSystem.AppDataDirectory, "freshmart-pos.db3");

    SQLiteAsyncConnection? _conn;
    readonly SemaphoreSlim _lock = new(1, 1);

    /// Before the rename to Yoe Yar POS, Windows kept the data under "User Name\com.freshmart.pos".
    /// Copy it across once so the shop keeps its sales and debts; the old folder is left untouched.
    static void CopyFromOldFolder()
    {
#if WINDOWS
        if (Environment.GetEnvironmentVariable("FRESHMART_DB") is { Length: > 0 } || File.Exists(DbPath)) return;
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "User Name", "com.freshmart.pos", "Data");
        if (File.Exists(Path.Combine(old, "freshmart-pos.db3"))) CopyDir(old, Path.GetDirectoryName(DbPath)!);
#endif
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: false);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public async Task<SQLiteAsyncConnection> GetAsync()
    {
        if (_conn is not null) return _conn;
        await _lock.WaitAsync();
        try
        {
            if (_conn is not null) return _conn;
            CopyFromOldFolder();
            var conn = new SQLiteAsyncConnection(DbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            // CreateTables also adds any new columns to existing tables
            await conn.CreateTablesAsync(CreateFlags.None,
                typeof(Product), typeof(Sale), typeof(SaleLine), typeof(StoreSettings), typeof(Customer), typeof(CustomerPayment));
            await conn.CreateTablesAsync(CreateFlags.None, typeof(Staff), typeof(CashClose), typeof(AuditEntry));
            await MigrateAsync(conn);
            if (await conn.Table<StoreSettings>().CountAsync() == 0)
                await conn.InsertAsync(new StoreSettings());
            if (await conn.Table<Product>().CountAsync() == 0)
                await conn.InsertAllAsync(DemoProducts());
            if (await conn.Table<Customer>().CountAsync() == 0)
                await conn.InsertAllAsync(DemoCustomers());
            _conn = conn;
            return conn;
        }
        finally { _lock.Release(); }
    }

    /// Fill sensible values for columns added after the first release.
    static async Task MigrateAsync(SQLiteAsyncConnection conn)
    {
        // sales made before credit existed were fully paid
        await conn.ExecuteAsync("UPDATE Sale SET Paid = Total WHERE Paid IS NULL");
        await conn.ExecuteAsync("UPDATE Sale SET CustomerId = COALESCE(CustomerId, 0), CustomerName = COALESCE(CustomerName, ''), Credit = COALESCE(Credit, 0), BalanceAfter = COALESCE(BalanceAfter, 0)");
        await conn.ExecuteAsync("UPDATE SaleLine SET UnitsPerPack = 1 WHERE UnitsPerPack IS NULL OR UnitsPerPack = 0");
        await conn.ExecuteAsync("UPDATE SaleLine SET NameEn = COALESCE(NameEn, ''), Pack = COALESCE(Pack, 0)");
        await conn.ExecuteAsync("UPDATE Product SET NameEn = COALESCE(NameEn, ''), CtnQty = COALESCE(CtnQty, 0), CtnPrice = COALESCE(CtnPrice, 0)");
        await conn.ExecuteAsync("UPDATE StoreSettings SET Language = 'my' WHERE Language IS NULL OR Language = ''");
        await conn.ExecuteAsync("UPDATE StoreSettings SET Phone = COALESCE(Phone, ''), SymbolAfter = COALESCE(SymbolAfter, 0), Decimals = COALESCE(Decimals, 2)");
        // release 3: cost price, staff, cash count
        await conn.ExecuteAsync("UPDATE Product SET Cost = COALESCE(Cost, 0)");
        await conn.ExecuteAsync("UPDATE SaleLine SET Cost = COALESCE(Cost, 0)");
        await conn.ExecuteAsync("UPDATE Sale SET StaffName = COALESCE(StaffName, '')");
        await conn.ExecuteAsync("UPDATE CustomerPayment SET StaffName = COALESCE(StaffName, '')");
        await conn.ExecuteAsync("UPDATE StoreSettings SET OpeningFloat = COALESCE(OpeningFloat, 0), MaxCashierDiscount = COALESCE(MaxCashierDiscount, 5), PaymentInfo = COALESCE(PaymentInfo, '')");
    }

    public static List<Product> DemoProducts() =>
    [
        P("8851959132012", "ကိုကာကိုလာ ၃၃၀ မီလီ", "Coca-Cola 330ml", "အအေး", 800, 400, 120, 24, 18000),
        P("8851959132029", "ရေသန့် ၁ လီတာ", "Purified Water 1L", "အအေး", 400, 600, 120, 12, 4200),
        P("8858998581153", "Shark စွမ်းအင်ဖျော်ရည်", "Shark Energy Drink", "အအေး", 600, 240, 48, 24, 13500),
        P("8850987101021", "မာမား ခေါက်ဆွဲ", "Mama Instant Noodles", "စားသောက်ကုန်", 500, 600, 150, 30, 13500),
        P("8850987101038", "ဘီစကွတ်", "Biscuits", "စားသောက်ကုန်", 1000, 96, 24, 24, 22000),
        P("8888196171019", "နို့ဆီ", "Condensed Milk", "စားသောက်ကုန်", 2000, 144, 48, 48, 92000),
        P("8851234567890", "ပရီးမီးယား ကော်ဖီမစ် (၃၀ ထုပ်)", "Premier Coffee Mix 30s", "စားသောက်ကုန်", 4500, 60, 12, 12, 51000),
        P("8859876543210", "နေကြာဆီ ၁ လီတာ", "Sunflower Oil 1L", "ဆီ/ဆန်", 9000, 36, 12, 12, 105000),
        P("2000101", "ဆန် (ပေါ်ဆန်း)", "Paw San Rice", "ဆီ/ဆန်", 3200, 200, 50, 0, 0, SellUnit.Kg),
        P("2000102", "ကြက်သွန်နီ", "Onion", "ဟင်းသီးဟင်းရွက်", 3500, 40, 10, 0, 0, SellUnit.Kg),
        P("2000103", "အာလူး", "Potato", "ဟင်းသီးဟင်းရွက်", 2500, 50, 10, 0, 0, SellUnit.Kg),
        P("2000104", "ကြက်ဥ", "Egg", "စားသောက်ကုန်", 350, 300, 60, 30, 10200),
        P("2000105", "သကြား ၁ ကီလို", "Sugar 1kg", "စားသောက်ကုန်", 4000, 40, 10, 20, 78000),
        P("8851932351234", "Lux ဆပ်ပြာ", "Lux Soap", "အိမ်သုံး", 1200, 192, 48, 48, 54000),
        P("8851932359999", "Colgate သွားတိုက်ဆေး", "Colgate Toothpaste", "အိမ်သုံး", 2500, 72, 24, 72, 170000),
        P("8851932350001", "အဝတ်လျှော်ဆပ်ပြာမှုန့်", "Detergent Powder", "အိမ်သုံး", 3000, 5, 10, 12, 34000),
    ];

    public static List<Customer> DemoCustomers() =>
    [
        new() { Name = "ဦးအောင်ကျော်", Phone = "09 450 123 456", Term = CreditTerm.Daily, CreditLimit = 50000 },
        new() { Name = "ဒေါ်မြင့်မြင့်", Phone = "09 777 888 999", Term = CreditTerm.Weekly, CreditLimit = 200000 },
        new() { Name = "ဒေါ်ခင်ခင်", Phone = "09 251 333 444", Term = CreditTerm.Monthly, CreditLimit = 300000 },
        new() { Name = "ကိုဇော်ဝင်း ကုန်စုံဆိုင်", Phone = "09 420 555 666", Address = "လှိုင်သာယာ", Term = CreditTerm.Monthly, CreditLimit = 2000000, IsWholesale = true },
    ];

    static Product P(string code, string name, string nameEn, string cat, decimal price, decimal stock, decimal reorder,
                     int ctnQty, decimal ctnPrice, SellUnit unit = SellUnit.Each) =>
        new() { Barcode = code, Name = name, NameEn = nameEn, Category = cat, Price = price, Cost = Math.Round(price * 0.85m / 10) * 10,
                Stock = stock, ReorderLevel = reorder,
                CtnQty = ctnQty, CtnPrice = ctnPrice, Unit = unit };
}
