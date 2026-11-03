using FreshMart.Pos.Data;
using FreshMart.Pos.Services;
using Microsoft.Extensions.Logging;

namespace FreshMart.Pos;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		builder.Services.AddSingleton<Loc>();
		builder.Services.AddSingleton<PosDatabase>();
		builder.Services.AddSingleton<LicenseService>();
		builder.Services.AddSingleton<AuthService>();
		builder.Services.AddSingleton<PosService>();
		builder.Services.AddSingleton<CartService>();
		builder.Services.AddSingleton<UiService>();
		builder.Services.AddSingleton<FileService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
