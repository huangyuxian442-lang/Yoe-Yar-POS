using System.ComponentModel.DataAnnotations;

namespace FreshMart.LicenseServer.Components;

public class AdminForm
{
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,30}$", ErrorMessage = "Username: 3–30 letters, digits, . _ -")]
    public string Username { get; set; } = "";

    [Required, MinLength(10, ErrorMessage = "Password: at least 10 characters.")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "The two passwords are different.")]
    public string Confirm { get; set; } = "";
}

public class ShopForm
{
    [Required(ErrorMessage = "Shop name is required.")] public string Name { get; set; } = "";
    public string? OwnerName { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Plan { get; set; } = Plans.Basic;
    [Range(1, 100)] public int MaxDevices { get; set; } = 1;
    [Range(0, 100)] public int MaxCashiers { get; set; } = 1;
    public DateOnly? LastDay { get; set; }
    public string? Notes { get; set; }
}
