namespace FreshMart.LicenseServer;

/// The server stores UTC; the admin panel shows and takes Myanmar time.
public static class MmTime
{
    static readonly TimeZoneInfo Zone = Find();

    static TimeZoneInfo Find()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Yangon"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("MMT", TimeSpan.FromHours(6.5), "MMT", "MMT"); }
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
    public static string Show(DateTime? utc) => utc is { } u ? Local(u).ToString("dd-MM-yyyy HH:mm") : "—";

    /// "Valid until 31-12-2026" means the whole of that day: stored as the next midnight, Myanmar time.
    public static DateOnly? LastDay(DateTime? expiresUtc) =>
        expiresUtc is { } u ? DateOnly.FromDateTime(Local(u).AddDays(-1)) : null;

    public static DateTime? FromLastDay(DateOnly? day) =>
        day is { } d ? TimeZoneInfo.ConvertTimeToUtc(d.AddDays(1).ToDateTime(TimeOnly.MinValue), Zone) : null;
}
