using System.Security.Cryptography;
using FreshMart.LicenseServer.Data;
using FreshMart.Licensing;
using Microsoft.EntityFrameworkCore;

namespace FreshMart.LicenseServer;

/// The private key that signs licences. Created on first start; back it up —
/// if it is lost, every installed app must be updated with a new public key.
public sealed class SigningKey : IDisposable
{
    public ECDsa Key { get; } = ECDsa.Create();
    public string PublicKeyBase64 { get; }
    public string Path { get; }

    public SigningKey(IConfiguration config, IHostEnvironment env)
    {
        Path = System.IO.Path.GetFullPath(config["License:SigningKeyPath"] ?? "App_Data/license-signing-key.pem", env.ContentRootPath);
        if (File.Exists(Path)) Key.ImportFromPem(File.ReadAllText(Path));
        else if (config.GetValue<bool>("License:RequireExistingKey"))
            // a hosted server must use the key the apps already trust; a new one would make every licence invalid
            throw new FileNotFoundException($"Licence signing key not found at {Path}. Copy license-signing-key.pem there.", Path);
        else
        {
            Key.GenerateKey(ECCurve.NamedCurves.nistP256);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, Key.ExportPkcs8PrivateKeyPem());
        }
        PublicKeyBase64 = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
    }

    public void Dispose() => Key.Dispose();
}

/// Activation and check-in for devices, plus the shop helpers the admin pages share.
public class LicenseManager(LicenseDb db, SigningKey signing, IConfiguration config)
{
    public async Task<(int Status, LicenseResponse Body)> ActivateAsync(ActivateRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.DeviceId) || r.DeviceId.Length > 100) return (400, new(null, LicenseErrors.BadRequest));
        var shop = await FindAsync(r.ShopCode);
        if (shop is null) return (404, new(null, LicenseErrors.CodeNotFound));
        if (shop.Suspended) return (403, new(null, LicenseErrors.Suspended));

        var now = DateTime.UtcNow;
        var device = shop.Devices.FirstOrDefault(d => d.Active && d.DeviceId == r.DeviceId);
        if (device is null)
        {
            // an ended subscription falls back to the Basic device count
            var allowed = shop.Expired(now) ? Plans.Base.Devices : shop.MaxDevices;
            if (shop.Devices.Count(d => d.Active) >= allowed) return (409, new(null, LicenseErrors.DeviceLimit));
            device = new Device { DeviceId = r.DeviceId, ActivatedAt = now };
            shop.Devices.Add(device);
            Log(shop.Id, "device", "device_activated", Trim(r.DeviceName, 100));
        }
        device.Name = Trim(r.DeviceName, 100);
        device.AppVersion = Trim(r.AppVersion, 30);
        device.LastSeenAt = now;
        await db.SaveChangesAsync();
        return (200, new(Issue(shop, device, now), null));
    }

    public async Task<(int Status, LicenseResponse Body)> CheckinAsync(CheckinRequest r)
    {
        var shop = await FindAsync(r.ShopCode);
        if (shop is null) return (404, new(null, LicenseErrors.CodeNotFound));
        var now = DateTime.UtcNow;
        var device = shop.Devices.FirstOrDefault(d => d.Active && d.DeviceId == r.DeviceId);
        if (device is null)
            // removed (or never activated): a signed "removed" licence tells the app to stop selling
            return (200, new(Issue(shop, new Device { DeviceId = r.DeviceId }, now, LicenseStatus.Removed), null));

        device.LastSeenAt = now;
        device.AppVersion = Trim(r.AppVersion, 30);
        shop.ActiveCashiers = Math.Max(0, r.ActiveCashiers);
        await db.SaveChangesAsync();
        return (200, new(Issue(shop, device, now), null));
    }

    string Issue(Shop shop, Device device, DateTime now, string? status = null)
    {
        var active = shop.Devices.Where(d => d.Active).OrderBy(d => d.ActivatedAt).ThenBy(d => d.Id).ToList();
        return LicenseToken.Sign(new LicenseInfo
        {
            ShopCode = shop.Code,
            ShopName = shop.Name,
            DeviceId = device.DeviceId,
            DeviceSlot = active.IndexOf(device) + 1,
            Plan = shop.Plan,
            MaxDevices = shop.MaxDevices,
            MaxCashiers = shop.MaxCashiers,
            ExpiresAt = shop.ExpiresAt is { } end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : null,   // the DB drops the kind
            BaseDevices = Plans.Base.Devices,
            BaseCashiers = Plans.Base.Cashiers,
            IssuedAt = now,
            Status = status ?? (shop.Suspended ? LicenseStatus.Suspended : LicenseStatus.Active),
            Support = config["Vendor:Support"] ?? "",
        }, signing.Key);
    }

    Task<Shop?> FindAsync(string code)
    {
        var key = LicenseToken.NormalizeCode(code);
        return db.Shops.Include(s => s.Devices).FirstOrDefaultAsync(s => s.CodeKey == key);
    }

    // ---------- helpers for the admin pages ----------
    const string CodeChars = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";   // no 0/O, 1/I/L

    public async Task<string> NewCodeAsync()
    {
        while (true)
        {
            var raw = RandomNumberGenerator.GetString(CodeChars, 8);
            if (!await db.Shops.AnyAsync(s => s.CodeKey == "YY" + raw)) return $"YY-{raw[..4]}-{raw[4..]}";
        }
    }

    public void Log(int? shopId, string by, string action, string detail) =>
        db.Activities.Add(new Activity { ShopId = shopId, By = by, Action = action, Detail = detail });

    static string Trim(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : t;
}

/// PBKDF2 password hashes for admin logins: "pbkdf2$iterations$salt$hash".
public static class Passwords
{
    const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out var iterations)) return false;
        var expected = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
