namespace RegulusMobile;

public partial class AppShell : Shell
{
	readonly RegulusPage regulusPage = new();
	readonly SearchPage searchPage = new();
	readonly JSystemPage jSystemPage = new();
	readonly SettingsPage settingsPage = new();

	public AppShell()
	{
		InitializeComponent();
		RegulusTab.Content = regulusPage;
		SearchTab.Content = searchPage;
		JSystemTab.Content = jSystemPage;
		SettingsTab.Content = settingsPage;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		jSystemPage.BeginStartupLogin();
	}
}
