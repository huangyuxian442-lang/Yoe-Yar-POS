using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreshMart.Licensing;

/// What a shop is allowed to do on one device. Issued and signed by the licence server,
/// checked offline by the POS app with the server's public key, so it can't be edited.
public record LicenseInfo
{
    public string ShopCode { get; init; } = "";       // "YY-7K3Q-92XD"
    public string ShopName { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public int DeviceSlot { get; init; }              // 1 = first active device of the shop
    public string Plan { get; init; } = "";
    public int MaxDevices { get; init; }
    public int MaxCashiers { get; init; }
    public DateTime? ExpiresAt { get; init; }         // UTC end of the subscription; null = no end
    public int BaseDevices { get; init; } = 1;        // what's left once a subscription ends
    public int BaseCashiers { get; init; } = 1;
    public DateTime IssuedAt { get; init; }           // UTC, last time the server confirmed this licence
    public string Status { get; init; } = LicenseStatus.Active;
    public string Support { get; init; } = "";        // how to reach the seller, shown in the app

    public bool SubscriptionEnded(DateTime utcNow) => ExpiresAt is { } end && utcNow >= end;
}

public static class LicenseStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";      // seller paused the shop (e.g. unpaid)
    public const string Removed = "removed";          // seller removed this device
}

public static class LicenseErrors
{
    public const string CodeNotFound = "code_not_found";
    public const string Suspended = "suspended";
    public const string DeviceLimit = "device_limit";
    public const string BadRequest = "bad_request";
}

public record ActivateRequest(string ShopCode, string DeviceId, string DeviceName, string AppVersion);
public record CheckinRequest(string ShopCode, string DeviceId, string AppVersion, int ActiveCashiers);
public record LicenseResponse(string? Token, string? Error);

/// Source-generated so it keeps working in trimmed Android/iOS release builds.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LicenseInfo))]
[JsonSerializable(typeof(ActivateRequest))]
[JsonSerializable(typeof(CheckinRequest))]
[JsonSerializable(typeof(LicenseResponse))]
public partial class LicenseJson : JsonSerializerContext;

/// Token = base64url(json) "." base64url(ECDSA P-256 / SHA-256 signature).
public static class LicenseToken
{
    public static string Sign(LicenseInfo info, ECDsa privateKey)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(info, LicenseJson.Default.LicenseInfo);
        return $"{B64(body)}.{B64(privateKey.SignData(body, HashAlgorithmName.SHA256))}";
    }

    /// The licence, or null when the token is damaged or not signed by our server.
    public static LicenseInfo? Verify(string? token, ECDsa publicKey)
    {
        try
        {
            var parts = token?.Split('.') ?? [];
            if (parts.Length != 2) return null;
            var body = FromB64(parts[0]);
            if (!publicKey.VerifyData(body, FromB64(parts[1]), HashAlgorithmName.SHA256)) return null;
            return JsonSerializer.Deserialize(body, LicenseJson.Default.LicenseInfo);
        }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException or ArgumentException) { return null; }
    }

    public static ECDsa PublicKey(string base64SubjectPublicKeyInfo)
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64SubjectPublicKeyInfo), out _);
        return key;
    }

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// Shop codes skip look-alike characters (0/O, 1/I/L) so they can be read out over the phone.
    public static string NormalizeCode(string? code) =>
        new string((code ?? "").ToUpperInvariant().Where(c => char.IsAsciiLetterOrDigit(c)).ToArray());
}
