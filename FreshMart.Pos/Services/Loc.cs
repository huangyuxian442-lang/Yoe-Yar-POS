using FreshMart.Pos.Localization;
using FreshMart.Pos.Models;

namespace FreshMart.Pos.Services;

/// UI language. Myanmar ("my") is the default; English ("en") is the alternative.
/// All text lives in Localization/Strings.cs so it can be corrected in one place.
public class Loc
{
    public string Lang { get; private set; } = "my";
    public bool IsMy => Lang != "en";
    public event Action? Changed;

    public void Set(string? lang)
    {
        var l = lang == "en" ? "en" : "my";
        if (l == Lang) return;
        Lang = l;
        Changed?.Invoke();
    }

    public string this[string key] =>
        Strings.Table.TryGetValue(key, out var s) ? (IsMy ? s.My : s.En) : key;

    public string F(string key, params object?[] args) => string.Format(this[key], args);

    /// Product name in the current language (falls back to the Myanmar name).
    public string Name(Product p) => !IsMy && p.NameEn.Length > 0 ? p.NameEn : p.Name;
    public string Name(CartLine l) => !IsMy && l.NameEn.Length > 0 ? l.NameEn : l.Name;
    public string Name(SaleLine l) => !IsMy && !string.IsNullOrEmpty(l.NameEn) ? l.NameEn : l.Name;

    public string Pack(PackType p) => p == PackType.Ctn ? this["ctn"] : this["pcs"];
    public string Term(CreditTerm t) => this["term_" + t.ToString().ToLowerInvariant()];
    public string Method(string m) => this["method_" + m.ToLowerInvariant()];

    static readonly string[] DaysMy = ["တနင်္ဂနွေ", "တနင်္လာ", "အင်္ဂါ", "ဗုဒ္ဓဟူး", "ကြာသပတေး", "သောကြာ", "စနေ"];
    static readonly string[] DaysEn = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    public string Day(DateTime d) => (IsMy ? DaysMy : DaysEn)[(int)d.DayOfWeek];
}
