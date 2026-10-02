using Android.Webkit;

namespace RegulusMobile;

[System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
sealed class ScheduleScriptClient : WebViewClient
{
	const string StartLine = "const start = addCalendarMonths(today, -1);";
	const string FutureLine = "const end = addCalendarMonths(today, 2);";
	const string PatchedStart = "const start = addCalendarMonths(today, -3);";

	static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
	static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);
	static readonly object Gate = new();

	readonly WebViewClient? inner;

	public ScheduleScriptClient(WebViewClient? inner)
	{
		this.inner = inner;
	}

	public static void Install(Android.Webkit.WebView native)
	{
		if (native.WebViewClient is ScheduleScriptClient) return;
		native.SetWebViewClient(new ScheduleScriptClient(native.WebViewClient));
	}

	public override WebResourceResponse? ShouldInterceptRequest(Android.Webkit.WebView? view, IWebResourceRequest? request)
	{
		var url = request?.Url?.ToString() ?? "";
		if (IsMobileScript(url))
		{
			var patched = LoadPatched(url);
			if (patched != null)
				return new WebResourceResponse("text/javascript", "UTF-8", new MemoryStream(patched));
		}
		if (inner != null) return inner.ShouldInterceptRequest(view, request);
		return base.ShouldInterceptRequest(view, request);
	}

	public override void OnPageStarted(Android.Webkit.WebView? view, string? url, Android.Graphics.Bitmap? favicon)
	{
		if (inner != null) inner.OnPageStarted(view, url, favicon);
		else base.OnPageStarted(view, url, favicon);
	}

	public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
	{
		if (inner != null) inner.OnPageFinished(view, url);
		else base.OnPageFinished(view, url);
	}

	public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, IWebResourceRequest? request)
	{
		if (inner != null) return inner.ShouldOverrideUrlLoading(view, request);
		return base.ShouldOverrideUrlLoading(view, request);
	}

	public override void OnReceivedError(Android.Webkit.WebView? view, IWebResourceRequest? request, WebResourceError? error)
	{
		if (inner != null) inner.OnReceivedError(view, request, error);
		else base.OnReceivedError(view, request, error);
	}

	public override void OnReceivedHttpError(Android.Webkit.WebView? view, IWebResourceRequest? request, WebResourceResponse? errorResponse)
	{
		if (inner != null) inner.OnReceivedHttpError(view, request, errorResponse);
		else base.OnReceivedHttpError(view, request, errorResponse);
	}

	static bool IsMobileScript(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		return uri.AbsolutePath.EndsWith("/mobile-web/mobile.js", StringComparison.OrdinalIgnoreCase);
	}

	static byte[]? LoadPatched(string url)
	{
		lock (Gate)
		{
			if (Cache.TryGetValue(url, out var cached)) return cached;
		}
		try
		{
			var bytes = Http.GetByteArrayAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
			var text = System.Text.Encoding.UTF8.GetString(bytes);
			if (!text.Contains(StartLine, StringComparison.Ordinal) || !text.Contains(FutureLine, StringComparison.Ordinal))
				return null;
			var patched = System.Text.Encoding.UTF8.GetBytes(text.Replace(StartLine, PatchedStart, StringComparison.Ordinal));
			lock (Gate) Cache[url] = patched;
			return patched;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			return null;
		}
	}
}
