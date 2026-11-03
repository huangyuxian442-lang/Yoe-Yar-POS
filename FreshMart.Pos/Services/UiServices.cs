using System.Globalization;
using System.Text;
using FreshMart.Pos.Models;

namespace FreshMart.Pos.Services;

public static class Fmt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string _symbol = "Ks";
    static bool _after = true;
    static int _decimals;

    public static void Configure(StoreSettings s)
    {
        _symbol = s.Currency ?? "";
        _after = s.SymbolAfter;
        _decimals = Math.Clamp(s.Decimals, 0, 3);
        PosMath.Decimals = _decimals;
    }

    /// Number with thousands separators and the shop's currency, e.g. "18,000 Ks".
    public static string Money(decimal v)
    {
        var n = PosMath.Money(v).ToString("N" + _decimals, Inv);
        if (_symbol.Length == 0) return n;
        return _after ? $"{n} {_symbol}" : _symbol + n;
    }

    public static string Num(decimal v, string format = "0.###") => v.ToString(format, Inv);
    public static string Qty(decimal q, SellUnit unit) => unit == SellUnit.Kg ? q.ToString("0.000", Inv) + " kg" : q.ToString("0.###", Inv);

    /// "3 CTN + 5 pcs" for carton products, otherwise plain quantity.
    public static string Pieces(decimal pieces, int ctnQty, SellUnit unit, Loc L)
    {
        if (unit == SellUnit.Kg) return Qty(pieces, unit);
        if (ctnQty <= 1 || Math.Abs(pieces) < ctnQty) return $"{Num(pieces)} {L["pcs"]}";
        var ctn = Math.Truncate(pieces / ctnQty);
        var rest = pieces - ctn * ctnQty;
        return rest == 0 ? $"{Num(ctn)} {L["ctn"]}" : $"{Num(ctn)} {L["ctn"]} + {Num(rest)} {L["pcs"]}";
    }

    public static string Stock(Product p, Loc L) => Pieces(p.Stock, p.HasCtn ? p.CtnQty : 0, p.Unit, L);

    public static string Csv(IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(v =>
            {
                var s = Convert.ToString(v, Inv) ?? "";
                return s.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            })));
        return sb.ToString();
    }
}

/// Toasts and native confirmation dialogs.
public class UiService(Loc L)
{
    public string? Toast { get; private set; }
    public event Action? Changed;
    CancellationTokenSource? _cts;

    public async void ShowToast(string message)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Toast = message; Changed?.Invoke();
        try { await Task.Delay(2600, cts.Token); } catch (TaskCanceledException) { return; }
        Toast = null; Changed?.Invoke();
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            Application.Current!.Windows[0].Page!.DisplayAlertAsync(title, message, L["yes"], L["no"]));
}

/// Saves exports to Documents on desktop; opens the share sheet on phones/tablets.
public class FileService(Loc L)
{
    public async Task<string> SaveAsync(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray(); // BOM so Excel reads UTF-8 / Myanmar text
#if WINDOWS || MACCATALYST
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppBrand.Name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        return L.F("saved_to", path);
#else
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = fileName, File = new ShareFile(path) });
        return L.F("saved_to", fileName);
#endif
    }

    /// Opens the system share sheet (Viber, Messenger, SMS… on phones; the Windows share dialog on PCs).
    public Task ShareTextAsync(string text, string title) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareTextRequest { Text = text, Title = title }));

    public async Task<string?> PickTextAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = L["choose_backup"] });
        if (result is null) return null;
        using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
