using Android.Webkit;
using Java.Interop;

namespace RegulusMobile;

public class ReceptionJsBridge : Java.Lang.Object
{
	readonly Action<string> onMessage;

	public ReceptionJsBridge(Action<string> onMessage)
	{
		this.onMessage = onMessage;
	}

	[JavascriptInterface]
	[Export("postReception")]
	public void PostReception(string? payload)
	{
		var text = payload ?? "";
		if (text.Length > (text.StartsWith("day:", StringComparison.Ordinal) ? 65536 : 16)) return;
		MainThread.BeginInvokeOnMainThread(() => onMessage(text));
	}
}
