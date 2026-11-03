using System.Threading.RateLimiting;
using FreshMart.LicenseServer;
using FreshMart.LicenseServer.Components;
using FreshMart.LicenseServer.Data;
using FreshMart.Licensing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite for trying it on this PC; PostgreSQL when hosted ("Database:Provider": "postgres")
var connection = builder.Configuration.GetConnectionString("Licenses") ?? "Data Source=App_Data/licenses.db";
builder.Services.AddDbContext<LicenseDb>(o =>
{
    if (builder.Configuration["Database:Provider"] == "postgres") o.UseNpgsql(connection);
    else o.UseSqlite(connection);
});
builder.Services.AddSingleton<SigningKey>();
builder.Services.AddScoped<LicenseManager>();
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.Cookie.Name = "fm_admin";
        o.Cookie.SameSite = SameSiteMode.Strict;
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    // devices and login: slows down anyone guessing shop codes or passwords
    o.AddPolicy("per-ip", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<LicenseDb>().Database.EnsureCreated();

var signing = app.Services.GetRequiredService<SigningKey>();
if (args.Contains("--public-key"))
{
    // paste this into FreshMart.Pos/Services/LicenseService.cs (PublicKey)
    Console.WriteLine(signing.PublicKeyBase64);
    return;
}
app.Logger.LogInformation("Licence public key: {Key}", signing.PublicKeyBase64);

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Redirect("/login");
}).RequireAuthorization();

var api = app.MapGroup("/api/v1").DisableAntiforgery().RequireRateLimiting("per-ip");
api.MapPost("/activate", async (ActivateRequest r, LicenseManager m) =>
{
    var (status, body) = await m.ActivateAsync(r);
    return Results.Json(body, statusCode: status);
});
api.MapPost("/checkin", async (CheckinRequest r, LicenseManager m) =>
{
    var (status, body) = await m.CheckinAsync(r);
    return Results.Json(body, statusCode: status);
});

app.Run();
