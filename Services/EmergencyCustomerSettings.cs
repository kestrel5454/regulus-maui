namespace RegulusMobile.Services;

public static class EmergencyCustomerSettings
{
	const string Key = "EmergencyCustomerCommentsEnabled";

	public static bool Enabled
	{
		get => Preferences.Default.Get(Key, false);
		set => Preferences.Default.Set(Key, value);
	}
}
