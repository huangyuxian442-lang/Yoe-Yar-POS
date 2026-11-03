namespace FreshMart.LicenseServer;

/// Plan presets. "Custom" keeps whatever limits are typed in the shop form.
/// When a paid subscription ends the shop falls back to the Basic limits; it is not locked out.
public static class Plans
{
    public const string Basic = "Basic";
    public const string Standard = "Standard";
    public const string Pro = "Pro";
    public const string Custom = "Custom";

    public record Limits(int Devices, int Cashiers);

    public static readonly Dictionary<string, Limits> Presets = new()
    {
        [Basic] = new(1, 1),
        [Standard] = new(2, 3),
        [Pro] = new(5, 10),
    };

    public static Limits Base => Presets[Basic];
    public static IEnumerable<string> All => [.. Presets.Keys, Custom];
}
