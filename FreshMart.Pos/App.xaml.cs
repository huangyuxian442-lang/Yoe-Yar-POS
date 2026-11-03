namespace FreshMart.Pos;

/// The product name shown to shops. Code and namespaces keep the original FreshMart.* names.
public static class AppBrand
{
	public const string Name = "Yoe Yar POS";
}

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = AppBrand.Name };
	}
}
