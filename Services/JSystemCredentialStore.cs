namespace RegulusMobile.Services;

public static class JSystemCredentialStore
{
	const string UserIdKey = "regulus.j-system.user-id";
	const string PasswordKey = "regulus.j-system.password";

	public static async Task SaveAsync(string userId, string password)
	{
		await SecureStorage.Default.SetAsync(UserIdKey, userId);
		await SecureStorage.Default.SetAsync(PasswordKey, password);
	}

	public static async Task<(string? UserId, string? Password)> LoadAsync()
	{
		var userId = await SecureStorage.Default.GetAsync(UserIdKey);
		var password = await SecureStorage.Default.GetAsync(PasswordKey);
		return (userId, password);
	}

	public static void Delete()
	{
		SecureStorage.Default.Remove(UserIdKey);
		SecureStorage.Default.Remove(PasswordKey);
	}
}
