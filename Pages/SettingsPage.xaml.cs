using RegulusMobile.Services;

namespace RegulusMobile;

public partial class SettingsPage : ContentPage
{
	bool loading;

	public SettingsPage()
	{
		InitializeComponent();
	}

	async void OnTryLoginClicked(object? sender, EventArgs e)
	{
		try
		{
			await Shell.Current.GoToAsync("//jsystem");
		}
		catch (Exception ex)
		{
			StatusLabel.Text = "Jシステム画面を開けませんでした（" + ex.GetType().Name + "）";
		}
		JSystemPage.Current?.OpenForManualLogin();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadStoredAsync();
	}

	async Task LoadStoredAsync()
	{
		loading = true;
		try
		{
			var stored = await JSystemCredentialStore.LoadAsync();
			var hasStored = !string.IsNullOrEmpty(stored.UserId) || !string.IsNullOrEmpty(stored.Password);
			SaveSwitch.IsToggled = hasStored;
			UserIdEntry.Text = stored.UserId ?? "";
			PasswordEntry.Text = stored.Password ?? "";
			StatusLabel.Text = hasStored ? "保存済みの認証情報を読み込みました" : "";
		}
		catch (Exception ex)
		{
			StatusLabel.Text = "認証情報を読み込めませんでした（" + ex.GetType().Name + "）";
		}
		finally
		{
			loading = false;
		}
	}

	async void OnSaveSwitchToggled(object? sender, ToggledEventArgs e)
	{
		if (loading || e.Value) return;
		await DeleteStoredAsync("保存をオフにしたため、保存済みの認証情報を削除しました");
	}

	async void OnSaveClicked(object? sender, EventArgs e)
	{
		if (!SaveSwitch.IsToggled)
		{
			await DeleteStoredAsync("保存はオフです。認証情報は保存していません");
			return;
		}
		var userId = UserIdEntry.Text ?? "";
		var password = PasswordEntry.Text ?? "";
		if (userId.Length == 0 || password.Length == 0)
		{
			StatusLabel.Text = "ユーザーIDとパスワードを入力してください";
			return;
		}
		try
		{
			await JSystemCredentialStore.SaveAsync(userId, password);
			StatusLabel.Text = "保存しました";
		}
		catch (Exception ex)
		{
			StatusLabel.Text = "保存できませんでした（" + ex.GetType().Name + "）";
		}
	}

	async void OnDeleteClicked(object? sender, EventArgs e)
	{
		loading = true;
		SaveSwitch.IsToggled = false;
		loading = false;
		await DeleteStoredAsync("保存した認証情報を削除しました");
	}

	async Task DeleteStoredAsync(string message)
	{
		try
		{
			JSystemCredentialStore.Delete();
			UserIdEntry.Text = "";
			PasswordEntry.Text = "";
			StatusLabel.Text = message;
			await Task.CompletedTask;
		}
		catch (Exception ex)
		{
			StatusLabel.Text = "削除できませんでした（" + ex.GetType().Name + "）";
		}
	}
}
