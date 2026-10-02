namespace RegulusMobile.Services;

public static class UrlPrivacy
{
	static readonly HashSet<string> HiddenQueryNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"password", "passwd", "pwd", "pass", "user", "userid", "username", "login", "id"
	};

	public static string ForDisplay(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return "未表示";
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "URLを表示できません";
		var path = uri.GetLeftPart(UriPartial.Path);
		if (string.IsNullOrEmpty(uri.Query) || uri.Query == "?") return path;
		var safe = new List<string>();
		foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = part.IndexOf('=');
			var rawName = eq >= 0 ? part[..eq] : part;
			var name = Uri.UnescapeDataString(rawName);
			if (HiddenQueryNames.Contains(name)) safe.Add(rawName + "=（非表示）");
			else safe.Add(part);
		}
		return safe.Count == 0 ? path : path + "?" + string.Join("&", safe);
	}

	public static string? ReceptionNo(string? url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query)) return null;
		foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = part.IndexOf('=');
			if (eq < 0) continue;
			var name = Uri.UnescapeDataString(part[..eq]);
			if (!name.Equals("no", StringComparison.OrdinalIgnoreCase)) continue;
			var value = Uri.UnescapeDataString(part[(eq + 1)..]);
			if (value.Length == 8 && value.All(char.IsDigit)) return value;
		}
		return null;
	}
}
