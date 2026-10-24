using Android.Webkit;

namespace RegulusMobile;

[System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
sealed class ScheduleScriptClient : WebViewClient
{
	const string StartLine = "const start = addCalendarMonths(today, -1);";
	const string OriginalFutureLine = "const end = addCalendarMonths(today, 2);";
	const string PatchedStart = "const start = addCalendarMonths(today, -3);";
	const string PatchedFuture = "const end = addCalendarMonths(today, 3);";
	const string VisitPersonScript = """

// Remember the card-view selection across WebView reloads and app restarts.
const regulusVisitPersonKey = 'regulusMobileVisitPerson';
let regulusSavedVisitPerson = '';
try { regulusSavedVisitPerson = localStorage.getItem(regulusVisitPersonKey) || ''; } catch (err) {}
const regulusFillVisitPeople = fillVisitPeople;
fillVisitPeople = function(scheduleData) {
  regulusFillVisitPeople(scheduleData);
  if (visitPerson && regulusSavedVisitPerson &&
      Array.from(visitPerson.options).some(option => option.value === regulusSavedVisitPerson)) {
    visitPerson.value = regulusSavedVisitPerson;
  }
};
if (visitPerson) visitPerson.addEventListener('change', () => {
  if (!visitPerson.value) return;
  regulusSavedVisitPerson = visitPerson.value;
  try { localStorage.setItem(regulusVisitPersonKey, regulusSavedVisitPerson); } catch (err) {}
});
""";
	const string DayManifestScript = """

// Keep retrieval in the card view and bind it to the rendered day/person snapshot.
let regulusRendered = null;
let regulusCacheState = { numbers: [], running: false };
const regulusCached = new Set();
const regulusToolbar = document.createElement('div');
regulusToolbar.id = 'regulus-day-cache';
const regulusFetch = document.createElement('button');
regulusFetch.id = 'regulus-day-fetch';
regulusFetch.type = 'button';
regulusFetch.textContent = '1日分取得';
regulusFetch.style.cssText = 'background:#0e5a8a;color:white;border:0;border-radius:10px;padding:8px 10px;font-size:16px;font-weight:bold;min-height:44px;width:100%;min-width:0;box-sizing:border-box';
const regulusProgress = document.createElement('div');
regulusProgress.setAttribute('role', 'status');
regulusProgress.style.cssText = 'padding:6px 0;font-size:13px';
const regulusPersonLabel = visitPerson && visitPerson.parentElement;
if (regulusPersonLabel && regulusPersonLabel.parentElement) {
  const row = document.createElement('div');
  row.id = 'regulus-person-actions';
  row.style.cssText = 'display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:10px;align-items:end';
  regulusPersonLabel.parentElement.insertBefore(row, regulusPersonLabel);
  regulusPersonLabel.style.minWidth = '0';
  visitPerson.style.boxSizing = 'border-box';
  row.append(regulusPersonLabel, regulusFetch);
} else {
  regulusToolbar.append(regulusFetch);
}
regulusToolbar.append(regulusProgress);
if (visitCards && visitCards.parentElement) visitCards.parentElement.insertBefore(regulusToolbar, visitCards);

function regulusReception(cell) {
  const text = parseComments(cell && cell.comments).map(item => String(item.text || '')).join('\n');
  const labeled = Array.from(new Set(Array.from(text.matchAll(/受付番号[：:]\s*([0-9]{8})(?!\d)/g), m => m[1])));
  if (labeled.length) return labeled.length === 1 ? labeled[0] : '';
  const plain = Array.from(new Set(Array.from(text.matchAll(/(?:^|\D)([0-9]{8})(?!\d)/g), m => m[1])));
  return plain.length === 1 ? plain[0] : '';
}
function regulusCurrent() {
  return !!(activeUser && mobileTab === 'visit' && regulusRendered && visitDate && visitPerson &&
    regulusRendered.date === visitDate.value && regulusRendered.person === visitPerson.value &&
    regulusRendered.loadToken === visitLoadToken);
}
function regulusPaintCache() {
  const current = regulusCurrent();
  regulusFetch.disabled = !regulusCacheState.running && !current;
  regulusFetch.textContent = regulusCacheState.running ? '取得を中止' : '1日分取得';
  const same = current && regulusCacheState.date === regulusRendered.date && regulusCacheState.person === regulusRendered.person;
  regulusProgress.textContent = same ? regulusCacheState.message || '' : '表示中の担当者の予定だけを取得します。取得済みの詳細はアプリ終了まで保持します。';
  if (!visitCards) return;
  Array.from(visitCards.children).forEach((card, index) => {
    const slot = current && regulusRendered.slots[index];
    const cached = slot && regulusCached.has(slot.number);
    let badge = card.querySelector('.regulus-cached');
    if (!cached) { if (badge) badge.remove(); return; }
    if (!badge) {
      badge = document.createElement('span');
      badge.className = 'regulus-cached';
      badge.textContent = '✓ 取得済み';
      badge.style.cssText = 'display:inline-block;background:#166534;color:#fff;border-radius:4px;padding:2px 6px;margin-top:4px;font-size:12px;font-weight:bold';
      card.appendChild(badge);
    }
  });
}
window.__regulusSetDayCacheState = function(state) {
  regulusCacheState = state;
  window.__regulusDayCacheRunning = state.running;
  regulusCached.clear();
  (state.numbers || []).forEach(number => regulusCached.add(number));
  regulusPaintCache();
};
const regulusRenderVisitCards = renderVisitCards;
renderVisitCards = function() {
  regulusRenderVisitCards();
  const person = selectedVisitPerson(visitScheduleData);
  regulusRendered = person ? {
    date: window.__regulusScheduleDate, person: person.name, loadToken: window.__regulusScheduleToken,
    slots: VISIT_SLOTS.map(slot => {
      const cell = person.cells[slot.offset];
      return { filled: !!cellText(cell).trim(), number: regulusReception(cell) };
    })
  } : null;
  regulusPaintCache();
};
regulusFetch.addEventListener('click', () => {
  if (!regulusFetch.disabled) window.RegulusNative.postReception('day-fetch');
});
// A day switch invalidates old cards immediately, before its asynchronous read finishes.
if (visitDate) visitDate.addEventListener('change', regulusPaintCache);
if (visitPerson) visitPerson.addEventListener('change', regulusPaintCache);
if (visitCards) ['click', 'touchend'].forEach(type => visitCards.addEventListener(type, event => {
  if (regulusCacheState.running) { event.preventDefault(); event.stopImmediatePropagation(); }
}, true));
window.__regulusReadDay = function(token) {
  const send = body => window.RegulusNative.postReception('day:' + JSON.stringify(body));
  if (!regulusCurrent()) { send({token, error: true}); return; }
  const numbers = new Set();
  let missing = 0;
  regulusRendered.slots.forEach(slot => {
    if (!slot.filled) return;
    if (slot.number) numbers.add(slot.number); else missing++;
  });
  send({token, date: regulusRendered.date, person: regulusRendered.person, numbers: Array.from(numbers), missing});
};
regulusPaintCache();
""";

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
			if (!text.Contains(StartLine, StringComparison.Ordinal)
				|| !text.Contains(OriginalFutureLine, StringComparison.Ordinal))
				return null;
			var patchedText = text
				.Replace("\"v\" + MOBILE_VERSION", "\"v\" + " + System.Text.Json.JsonSerializer.Serialize(Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString), StringComparison.Ordinal)
				.Replace(StartLine, PatchedStart, StringComparison.Ordinal)
				.Replace(OriginalFutureLine, PatchedFuture, StringComparison.Ordinal)
				.Replace("visitScheduleData = scheduleData;", "visitScheduleData = scheduleData; window.__regulusScheduleDate = dateIso; window.__regulusScheduleToken = token;", StringComparison.Ordinal) + VisitPersonScript + DayManifestScript;
			var patched = System.Text.Encoding.UTF8.GetBytes(patchedText);
			lock (Gate) Cache[url] = patched;
			return patched;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			return null;
		}
	}
}
