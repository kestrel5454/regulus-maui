using Microsoft.Extensions.DependencyInjection;

namespace RegulusMobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		window.Deactivated += (_, _) => JSystemPage.Current?.OnAppDeactivated();
		window.Stopped += (_, _) => JSystemPage.Current?.OnAppDeactivated();
		window.Activated += (_, _) => JSystemPage.Current?.OnAppActivated();
		window.Resumed += (_, _) => JSystemPage.Current?.OnAppActivated();
		return window;
	}
}
