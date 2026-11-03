using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreshMart.Licensing;
using FreshMart.Pos.Data;

namespace FreshMart.Pos.Services;

/// The shop's licence on this device: activation with a shop code, offline checks against
/// the server's signed licence, and a check-in whenever there is internet.
public class LicenseService
{
    // public key of the licence server (`dotnet run -- --public-key` in FreshMart.LicenseServer)
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEf5uYF9gPqXB3nLYZtB8KnxtIbFDts11Nf9IkRMr3zdnWmrcl6Kc7VRGOS8+rNyBqYxNZ6w8oL23zY0aWQRvFUw==";
    // TODO before release: your hosted server's https address. FRESHMART_LICENSE_URL overrides it for testing.
    const string DefaultServer = "http://localhost:5080";

    public const int OfflineDays = 30;    // selling continues this long without reaching the server
    public const int WarnDays = 7;

    public static string ServerUrl => Environment.GetEnvironmentVariable("FRESHMART_LICENSE_URL") is { Length: > 0 } url
        ? url.TrimEnd('/') : DefaultServer;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly ECDsa _key = LicenseToken.PublicKey(PublicKey);

    public string DeviceId { get; } = DeviceIdentity.Get();
    public LicenseInfo? Info { get; private set; }
    DateTime _lastSeen;
    public event Action? Changed;

    static string FilePath => Path.Combine(Path.GetDirectoryName(PosDatabase.DbPath)!, "license.json");
    static DateTime Now => DateTime.UtcNow;

    // ---------- state ----------
    public bool IsActivated => Info is not null;
    public bool Ended => Info?.SubscriptionEnded(Now) == true;
    public int MaxDevices => Info is null ? 0 : Ended ? Info.BaseDevices : Info.MaxDevices;
    public int MaxCashiers => Info is null ? 0 : Ended ? Info.BaseCashiers : Info.MaxCashiers;
    public int DaysOffline => Info is null ? 0 : Math.Max(0, (int)(Now - Info.IssuedAt).TotalDays);
    /// Days until the last paid day (0 = today is the last day).
    public int? DaysLeft => Info?.ExpiresAt is { } end && !Ended ? (end.ToLocalTime().Date.AddDays(-1) - DateTime.Today).Days : null;

    /// Why this device may not sell right now (a Strings key), or null when it may.
    public string? LockReason => Info switch
    {
        null => "lic_not_activated",
        { Status: LicenseStatus.Suspended } => "lic_err_suspended",
        { Status: LicenseStatus.Removed } => "lic_removed",
        _ when Now < _lastSeen.AddDays(-1) => "lic_clock",       // clock was set back
        _ when DaysOffline >= OfflineDays => "lic_offline",
        { } i when i.DeviceSlot > MaxDevices => "lic_device_over",
        _ => null,
    };

    public bool CanSell => LockReason is null;

    // ---------- storage ----------
    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var s = JsonSerializer.Deserialize(File.ReadAllText(FilePath), StoredJson.Default.StoredLicense);
            if (s is null) return;
            _lastSeen = s.LastSeenUtc;
            Info = Accept(s.Token);
            Save(s.Token);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Info = null; }
    }

    /// A valid licence for this device, or null.
    LicenseInfo? Accept(string? token) =>
        LicenseToken.Verify(token, _key) is { } info && info.DeviceId == DeviceId ? info : null;

    void Save(string token)
    {
        if (Now > _lastSeen) _lastSeen = Now;
        if (Info is not null && Info.IssuedAt > _lastSeen) _lastSeen = Info.IssuedAt;
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new StoredLicense(token, _lastSeen), StoredJson.Default.StoredLicense));
        File.Move(tmp, FilePath, overwrite: true);
    }

    // ---------- server ----------
    /// Returns a Strings key for the error, or null when activated.
    public async Task<string?> ActivateAsync(string code)
    {
        if (LicenseToken.NormalizeCode(code).Length < 6) return "lic_err_code_not_found";
        var (token, error) = await PostAsync("activate", JsonContent.Create(
            new ActivateRequest(code.Trim(), DeviceId, DeviceName, AppVersion), LicenseJson.Default.ActivateRequest));
        if (error is not null) return error;
        var info = Accept(token);
        if (info is null) return "lic_err_invalid";
        Info = info;
        Save(token!);
        Changed?.Invoke();
        return null;
    }

    /// Picks up plan changes from the server. Returns a Strings key for the error, or null.
    public async Task<string?> CheckInAsync(int activeCashiers)
    {
        if (Info is null) return "lic_not_activated";
        var (token, error) = await PostAsync("checkin", JsonContent.Create(
            new CheckinRequest(Info.ShopCode, DeviceId, AppVersion, activeCashiers), LicenseJson.Default.CheckinRequest));
        if (error is not null) return error;
        var info = Accept(token);
        if (info is null) return "lic_err_invalid";
        Info = info;
        Save(token!);
        Changed?.Invoke();
        return null;
    }

    async Task<(string? Token, string? Error)> PostAsync(string path, HttpContent body)
    {
        try
        {
            using var res = await Http.PostAsync($"{ServerUrl}/api/v1/{path}", body);
            var r = await res.Content.ReadFromJsonAsync(LicenseJson.Default.LicenseResponse);
            return r?.Token is { } t ? (t, null) : (null, "lic_err_" + (r?.Error ?? "invalid"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return (null, "lic_err_offline");
        }
    }

    static string AppVersion => AppInfo.Current.VersionString;
    static string DeviceName => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model} · {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString} · {DeviceInfo.Current.Name}";
}

record StoredLicense(string Token, DateTime LastSeenUtc);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StoredLicense))]
partial class StoredJson : JsonSerializerContext;

/// A stable id for this phone/PC that survives reinstalling the app, so a reinstall
/// doesn't use up another device slot and copying the data folder to another machine doesn't clone it.
static class DeviceIdentity
{
    public static string Get()
    {
        string? raw = null;
        try
        {
#if ANDROID
            raw = Android.Provider.Settings.Secure.GetString(Android.App.Application.Context.ContentResolver, Android.Provider.Settings.Secure.AndroidId);
#elif WINDOWS
            raw = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
#endif
        }
        catch (Exception) { raw = null; }   // fall back to an id kept with the app's data

        if (string.IsNullOrEmpty(raw))
        {
            var file = Path.Combine(FileSystem.AppDataDirectory, "device-id");
            if (File.Exists(file)) raw = File.ReadAllText(file).Trim();
            if (string.IsNullOrEmpty(raw)) File.WriteAllText(file, raw = Guid.NewGuid().ToString("N"));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("freshmart-pos:" + raw)))[..32].ToLowerInvariant();
    }
}
