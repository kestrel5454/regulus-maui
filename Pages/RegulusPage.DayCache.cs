using System.Text.Json;

namespace RegulusMobile;

public partial class RegulusPage
{
	readonly HashSet<string> cachedDayNumbers = new(StringComparer.Ordinal);
	bool dayCacheRunning;
	int dayCacheGeneration;
	TaskCompletionSource<DayManifest?>? pendingDayManifest;
	DayManifest? currentDayManifest;
	string dayCacheMessage = "";

	sealed record DayManifest(string Date, string Person, List<string> Numbers, int Missing);

	async Task PublishDayCacheAsync()
	{
		try
		{
			await RegulusWeb.EvaluateJavaScriptAsync(
				"window.__regulusSetDayCacheState&&window.__regulusSetDayCacheState(" +
				JsonSerializer.Serialize(new
				{
					date = currentDayManifest?.Date,
					person = currentDayManifest?.Person,
					message = dayCacheMessage,
					running = dayCacheRunning,
					numbers = cachedDayNumbers.ToArray()
				}) + ")");
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) { }
	}

	void CancelDayCache()
	{
		dayCacheGeneration++;
		dayCacheRunning = false;
		pendingDayManifest?.TrySetResult(null);
		pendingDayManifest = null;
		dayCacheMessage = "取得を中止しました。取得済みの詳細は保持しています。";
		_ = PublishDayCacheAsync();
	}

	async Task FetchDayCacheAsync()
	{
		if (dayCacheRunning) return;
		var page = JSystemPage.Current;
		dayCacheRunning = true;
		var generation = ++dayCacheGeneration;
		try
		{
			// Snapshot only the cards currently rendered, including the selected person.
			var completion = new TaskCompletionSource<DayManifest?>(TaskCreationOptions.RunContinuationsAsynchronously);
			pendingDayManifest = completion;
			await RegulusWeb.EvaluateJavaScriptAsync("window.__regulusReadDay&&window.__regulusReadDay(" + generation + ")");
			var manifest = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
			if (generation != dayCacheGeneration) return;
			if (manifest == null) throw new InvalidOperationException();
			currentDayManifest = manifest;
			if (page == null) throw new InvalidOperationException();
			var succeeded = 0;
			var failed = 0;
			foreach (var number in manifest.Numbers)
			{
				if (generation != dayCacheGeneration) return;
				dayCacheMessage = $"取得中 {succeeded + failed}/{manifest.Numbers.Count}件（失敗 {failed}件）";
				await PublishDayCacheAsync();
				if (page.TryGetCachedCustomer(number, out _))
				{
					cachedDayNumbers.Add(number);
					succeeded++;
					continue;
				}
				var detailCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				page.ReadFromRegulus(number, ++requestToken, (doneToken, status, card) =>
				{
					if (status == "ok" && card != null)
					{
						cachedDayNumbers.Add(number);
						_ = PublishDayCacheAsync();
					}
					detailCompletion.TrySetResult(status == "ok" && card != null);
				});
				var success = await detailCompletion.Task.WaitAsync(TimeSpan.FromMinutes(2));
				if (generation != dayCacheGeneration) return;
				if (success) succeeded++; else failed++;
			}
			dayCacheMessage = $"取得済み {succeeded}/{manifest.Numbers.Count}件・失敗 {failed}件・受付番号なし {manifest.Missing}枠。";
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			if (generation == dayCacheGeneration) dayCacheMessage = "取得できませんでした。通信状態・ログインとカードの読み込みを確認して再度取得してください。";
		}
		finally
		{
			if (generation == dayCacheGeneration)
			{
				pendingDayManifest = null;
				dayCacheRunning = false;
				await PublishDayCacheAsync();
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
			if (root.TryGetProperty("error", out _)) pendingDayManifest?.TrySetResult(null);
			else
			{
				var numbers = root.GetProperty("numbers").EnumerateArray().Select(item => item.GetString() ?? "")
					.Where(number => number.Length == 8 && number.All(char.IsAsciiDigit)).Distinct().ToList();
				pendingDayManifest?.TrySetResult(new DayManifest(root.GetProperty("date").GetString() ?? "",
					root.GetProperty("person").GetString() ?? "", numbers, root.GetProperty("missing").GetInt32()));
			}
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { pendingDayManifest?.TrySetResult(null); }
		return true;
	}
}
