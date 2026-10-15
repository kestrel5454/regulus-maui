using System.Globalization;
using System.Text.Json;

namespace RegulusMobile;

public partial class RegulusPage
{
	readonly Dictionary<string, DayManifest> dayManifests = new(StringComparer.Ordinal);
	DateTime dayCacheDate = DateTime.Today;
	bool dayCachePanelOpen;
	bool dayCacheRunning;
	int dayCacheGeneration;
	TaskCompletionSource<DayManifest?>? pendingDayManifest;
	string dayCacheMessage = "日付を選び、通信できる場所で取得してください。";

	sealed record DayManifest(string Date, List<string> Numbers, int Missing);

	async void OnOpenDayCacheClicked(object? sender, EventArgs e)
	{
		if (dayCacheRunning || !DayFetchButton.IsEnabled) return;
		DayFetchButton.IsEnabled = false;
		try
		{
			try
			{
				var date = UnwrapJs(await RegulusWeb.EvaluateJavaScriptAsync("document.getElementById('visit-date')?.value||''"));
				if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected)) dayCacheDate = selected;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException) { }
			requestToken++;
			dayCachePanelOpen = true;
			RenderDayCache();
			await FetchDayCacheAsync();
		}
		finally { DayFetchButton.IsEnabled = true; }
	}

	void RenderDayCache()
	{
		if (!dayCachePanelOpen) return;
		CustomerPanel.IsVisible = true;
		BridgeStatusLabel.Text = "1日分の顧客詳細";
		HeaderReceptionLabel.Text = "メモリのみ";
		RegulusCustomerStack.Children.Clear();
		var picker = new DatePicker { Date = dayCacheDate, Format = "yyyy/MM/dd", IsEnabled = !dayCacheRunning };
		picker.DateSelected += (_, e) =>
		{
			if (e.NewDate is not DateTime date) return;
			dayCacheDate = date;
			dayCacheMessage = "日付を選び、通信できる場所で取得してください。";
			RenderDayCache();
		};
		RegulusCustomerStack.Children.Add(picker);
		var fetch = new Button { Text = dayCacheRunning ? "取得を中止" : "この日の顧客詳細を取得" };
		fetch.Clicked += async (_, _) =>
		{
			if (dayCacheRunning) CancelDayCache();
			else await FetchDayCacheAsync();
		};
		RegulusCustomerStack.Children.Add(fetch);
		RegulusCustomerStack.Children.Add(new Label { Text = "全担当者の予定が対象です。取得済みの詳細は圏外でも下の一覧から開けます。アプリのプロセス終了で消え、端末のファイルには保存しません。", FontSize = 13 });
		RegulusCustomerStack.Children.Add(new Label { Text = dayCacheMessage, FontSize = 16 });
		var key = dayCacheDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		if (!dayManifests.TryGetValue(key, out var manifest)) return;
		foreach (var number in manifest.Numbers)
		{
			var page = JSystemPage.Current;
			var cached = page != null && page.TryGetCachedCustomer(number, out var value) ? value : null;
			var button = new Button
			{
				Text = cached == null ? number + "（未取得）" : number + "　" + cached.CustomerName + "（取得済み）",
				IsEnabled = cached != null,
				HorizontalOptions = LayoutOptions.Fill
			};
			button.Clicked += (_, _) =>
			{
				if (cached == null || page == null) return;
				dayCachePanelOpen = false;
				RegulusCustomerStack.Children.Clear();
				var back = new Button { Text = "← 取得済み一覧" };
				back.Clicked += (_, _) => { dayCachePanelOpen = true; RenderDayCache(); };
				RegulusCustomerStack.Children.Add(back);
				RegulusCustomerStack.Children.Add(page.CreateSharedCustomerView(cached));
				BridgeStatusLabel.Text = "顧客情報（キャッシュ）";
				HeaderReceptionLabel.Text = number;
			};
			RegulusCustomerStack.Children.Add(button);
		}
	}

	void CancelDayCache()
	{
		dayCacheGeneration++;
		dayCacheRunning = false;
		pendingDayManifest?.TrySetResult(null);
		pendingDayManifest = null;
		dayCacheMessage = "取得を中止しました。取得済みの詳細は保持しています。";
		RenderDayCache();
	}

	async Task FetchDayCacheAsync()
	{
		var page = JSystemPage.Current;
		if (page == null) return;
		dayCacheRunning = true;
		var generation = ++dayCacheGeneration;
		var date = dayCacheDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		dayCacheMessage = "この日の予定を読み込み中...";
		RenderDayCache();
		try
		{
			// Reuse the in-memory day's index when offline or retrying partial downloads.
			if (!dayManifests.TryGetValue(date, out var manifest))
			{
				var completion = new TaskCompletionSource<DayManifest?>(TaskCreationOptions.RunContinuationsAsynchronously);
				pendingDayManifest = completion;
				await RegulusWeb.EvaluateJavaScriptAsync("window.__regulusReadDay&&window.__regulusReadDay(" + JsonSerializer.Serialize(date) + "," + generation + ")");
				manifest = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
				if (generation != dayCacheGeneration) return;
				if (manifest == null) throw new InvalidOperationException();
				dayManifests[date] = manifest;
			}
			var succeeded = 0;
			var failed = 0;
			foreach (var number in manifest.Numbers)
			{
				if (generation != dayCacheGeneration) return;
				dayCacheMessage = $"取得中 {succeeded + failed}/{manifest.Numbers.Count}件（失敗 {failed}件）";
				RenderDayCache();
				if (page.TryGetCachedCustomer(number, out _)) { succeeded++; continue; }
				var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				page.ReadFromRegulus(number, ++requestToken, (_, status, card) => completion.TrySetResult(status == "ok" && card != null));
				var success = await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
				if (generation != dayCacheGeneration) return;
				if (success) succeeded++; else failed++;
			}
			dayCacheMessage = $"取得済み {succeeded}/{manifest.Numbers.Count}件・失敗 {failed}件・受付番号なし {manifest.Missing}枠。未取得分は再度取得できます。";
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			if (generation == dayCacheGeneration) dayCacheMessage = "取得できませんでした。通信状態とログインを確認して再度取得してください。取得済みの詳細は保持しています。";
		}
		finally
		{
			if (generation == dayCacheGeneration)
			{
				pendingDayManifest = null;
				dayCacheRunning = false;
				RenderDayCache();
			}
		}
	}

	bool TryHandleDayManifest(string payload)
	{
		if (!payload.StartsWith("day:", StringComparison.Ordinal)) return false;
		try
		{
			using var doc = JsonDocument.Parse(payload[4..]);
			var root = doc.RootElement;
			if (!dayCacheRunning || root.GetProperty("token").GetInt32() != dayCacheGeneration) return true;
			var date = root.GetProperty("date").GetString() ?? "";
			if (date != dayCacheDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) return true;
			if (root.TryGetProperty("error", out _)) pendingDayManifest?.TrySetResult(null);
			else
			{
				var numbers = root.GetProperty("numbers").EnumerateArray().Select(item => item.GetString() ?? "")
					.Where(number => number.Length == 8 && number.All(char.IsAsciiDigit)).Distinct().ToList();
				pendingDayManifest?.TrySetResult(new DayManifest(date, numbers, root.GetProperty("missing").GetInt32()));
			}
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { pendingDayManifest?.TrySetResult(null); }
		return true;
	}
}
