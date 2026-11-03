using Microsoft.EntityFrameworkCore;

namespace FreshMart.LicenseServer.Data;

/// A customer shop: who they are and what their plan allows.
public class Shop
{
    public int Id { get; set; }
    public string Code { get; set; } = "";            // "YY-7K3Q-92XD", typed into the app to activate
    public string CodeKey { get; set; } = "";         // Code without dashes, for lookups
    public string Name { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Plan { get; set; } = Plans.Basic;
    public int MaxDevices { get; set; } = 1;
    public int MaxCashiers { get; set; } = 1;
    public DateTime? ExpiresAt { get; set; }          // UTC end of the subscription; null = Basic, no end
    public bool Suspended { get; set; }
    public string Notes { get; set; } = "";
    public int ActiveCashiers { get; set; }           // as last reported by the shop's devices
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Device> Devices { get; set; } = [];

    public bool Expired(DateTime utcNow) => ExpiresAt is { } end && utcNow >= end;
}

/// One phone, tablet or PC activated for a shop.
public class Device
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public Shop? Shop { get; set; }
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }          // freed slot; the app on that device stops selling

    public bool Active => RemovedAt is null;
}

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Activity
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public int? ShopId { get; set; }
    public string By { get; set; } = "";              // admin username, or "device"
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class LicenseDb(DbContextOptions<LicenseDb> options) : DbContext(options)
{
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<AdminUser> Admins => Set<AdminUser>();
    public DbSet<Activity> Activities => Set<Activity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Shop>().HasIndex(s => s.CodeKey).IsUnique();
        b.Entity<Device>().HasIndex(d => new { d.ShopId, d.DeviceId });
        b.Entity<AdminUser>().HasIndex(a => a.Username).IsUnique();
        b.Entity<Activity>().HasIndex(a => a.At);
    }
}
