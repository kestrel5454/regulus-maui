using System.Text.Json;
using RegulusMobile.Services;

namespace RegulusMobile;

public partial class RegulusPage
{
	bool emergencyMode;

	async Task PublishEmergencyModeAsync()
	{
		try
		{
			await RegulusWeb.EvaluateJavaScriptAsync("window.__regulusEmergency=" +
				(EmergencyCustomerSettings.Enabled ? "true" : "false") + ";" +
				"['regulus-day-fetch','regulus-day-cache'].forEach(function(id){" +
				"var el=document.getElementById(id);if(el)el.hidden=window.__regulusEmergency;});");
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) { }
	}

	void ReadEmergencyCustomer(string payload)
	{
		++requestToken; // Invalidate an earlier J-system callback.
		try
		{
			using var document = JsonDocument.Parse(payload["emergency:".Length..]);
			var root = document.RootElement;
			var text = root.GetProperty("text").GetString() ?? "";
			var number = root.GetProperty("number").GetString() ?? "";
			if (!EmergencyCustomerParser.TryParse(text, number, out var fields))
			{
				ShowStatus("2つ目のコメントに顧客詳細がないか、項目を読み取れませんでした");
				return;
			}
			var page = JSystemPage.Current;
			if (page == null) { ShowStatus("顧客情報取得失敗"); return; }
			RegulusCustomerStack.Children.Clear();
			RegulusCustomerStack.Children.Add(page.CreateEmergencyCustomerView(fields));
			HeaderReceptionLabel.Text = fields["receptionNo"];
			ShowStatus("顧客情報取得成功");
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
			or System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			ShowStatus("2つ目のコメントから顧客詳細を読み取れませんでした");
		}
	}
}
