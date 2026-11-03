using System.Security.Cryptography;
using System.Text;
using FreshMart.Pos.Data;
using FreshMart.Pos.Models;

namespace FreshMart.Pos.Services;

/// Who is using the till, owner-only approvals, and the audit log.
public class AuthService(PosDatabase database, LicenseService license)
{
    public Staff? Current { get; private set; }
    public bool IsOwner => Current?.IsOwner == true;
    public string CurrentName => Current?.Name ?? "";

    /// Set while an owner's PIN is being asked for; the layout shows the approval dialog.
    public string? PendingApproval { get; private set; }
    TaskCompletionSource<bool>? _approval;

    public event Action? Changed;
    void Notify() => Changed?.Invoke();

    // ---------- staff ----------
    public async Task<List<Staff>> GetStaffAsync(bool includeInactive = false)
    {
        var db = await database.GetAsync();
        var all = await db.Table<Staff>().OrderByDescending(s => s.Role).ThenBy(s => s.Name).ToListAsync();
        return includeInactive ? all : all.Where(s => s.Active).ToList();
    }

    public async Task<bool> HasStaffAsync() => await (await database.GetAsync()).Table<Staff>().CountAsync() > 0;

    public static bool ValidPin(string? pin) => pin is { Length: >= 4 and <= 8 } && pin.All(char.IsDigit);

    /// Adds or updates a staff member. A null/empty PIN keeps the existing one. Returns an error key or null.
    public async Task<string?> SaveStaffAsync(Staff s, string? newPin)
    {
        if (string.IsNullOrWhiteSpace(s.Name)) return "err_staff_name";
        if (s.Id == 0 && !ValidPin(newPin)) return "err_pin";
        if (!string.IsNullOrEmpty(newPin) && !ValidPin(newPin)) return "err_pin";
        var db = await database.GetAsync();
        var all = await db.Table<Staff>().ToListAsync();
        // never leave the shop without an active owner
        var others = all.Where(x => x.Id != s.Id && x.Active && x.IsOwner).Count();
        if (others == 0 && all.Any(x => x.IsOwner) && (!s.IsOwner || !s.Active)) return "err_last_owner";
        // the plan allows the owner plus MaxCashiers accounts; only block changes that add one
        var wasActive = all.FirstOrDefault(x => x.Id == s.Id)?.Active == true;
        if (s.Active && !wasActive && all.Count(x => x.Id != s.Id && x.Active) >= StaffAllowed) return "err_plan_cashiers";

        s.Name = s.Name.Trim();
        if (!string.IsNullOrEmpty(newPin)) (s.PinHash, s.PinSalt) = Hash(newPin);
        if (s.Id == 0) await db.InsertAsync(s); else await db.UpdateAsync(s);
        if (Current?.Id == s.Id) Current = s;
        await LogAsync("staff", $"{s.Name} ({s.Role}){(s.Active ? "" : " — off")}");
        return null;
    }

    /// Owner + the plan's cashier accounts.
    int StaffAllowed => 1 + license.MaxCashiers;

    /// After a downgrade there can be more accounts than the plan allows: owners first, then the oldest
    /// cashiers keep signing in; the rest wait until the plan is upgraded or someone is switched off.
    public async Task<bool> WithinPlanAsync(Staff s) =>
        (await GetStaffAsync()).OrderByDescending(x => x.IsOwner).ThenBy(x => x.Id).Take(StaffAllowed).Any(x => x.Id == s.Id);

    public async Task<int> CountActiveCashiersAsync() => (await GetStaffAsync()).Count(s => !s.IsOwner);

    static (string Hash, string Salt) Hash(string pin)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        return (HashWith(pin, salt), salt);
    }

    static string HashWith(string pin, string salt) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(salt + ":" + pin)));

    static bool Check(Staff s, string pin) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(HashWith(pin, s.PinSalt)), Encoding.UTF8.GetBytes(s.PinHash));

    // ---------- login ----------
    public async Task<bool> LoginAsync(Staff s, string pin)
    {
        if (!s.Active || !Check(s, pin)) { await Task.Delay(600); return false; } // slow down guessing
        Current = s;
        await LogAsync("login", s.Name);
        Notify();
        return true;
    }

    public void Lock()
    {
        Current = null;
        CancelApproval();
        Notify();
    }

    // ---------- owner approval ----------
    /// True straight away for an owner; otherwise asks for any owner's PIN.
    public async Task<bool> RequireOwnerAsync(string actionLabel)
    {
        if (IsOwner) return true;
        _approval?.TrySetResult(false);
        _approval = new TaskCompletionSource<bool>();
        PendingApproval = actionLabel;
        Notify();
        var ok = await _approval.Task;
        PendingApproval = null;
        Notify();
        return ok;
    }

    public async Task<bool> ApproveAsync(string pin)
    {
        var owner = (await GetStaffAsync()).FirstOrDefault(s => s.IsOwner && Check(s, pin));
        if (owner is null) { await Task.Delay(600); return false; }
        await LogAsync("override", $"{owner.Name} → {PendingApproval}");
        _approval?.TrySetResult(true);
        return true;
    }

    public void CancelApproval() => _approval?.TrySetResult(false);

    // ---------- audit ----------
    public async Task LogAsync(string action, string detail)
    {
        var db = await database.GetAsync();
        await db.InsertAsync(new AuditEntry { At = DateTime.Now, StaffName = CurrentName, Action = action, Detail = detail });
    }

    public async Task<List<AuditEntry>> GetAuditAsync(int take = 300)
    {
        var db = await database.GetAsync();
        return await db.Table<AuditEntry>().OrderByDescending(a => a.At).Take(take).ToListAsync();
    }
}
