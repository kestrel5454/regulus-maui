using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Platform;
using RegulusMobile.Services;

namespace RegulusMobile;

public partial class JSystemPage : ContentPage
{
	const string HomeUrl = "https://service.joshin.co.jp";
	const string LoginUrl = "https://service.joshin.co.jp/pc_support/trader/top/login.php";
	const string CustomerUrlPrefix = "https://service.joshin.co.jp/pc_support/trader/visit/entry_disp_sub.php?no=";
	const string SearchUrl = "https://service.joshin.co.jp/pc_support/trader/search/search_reception.php";

	public static JSystemPage? Current { get; private set; }

	string currentUrl = "";
	string viewState = "未表示";
	const int MaxLoginAttempts = 3;

	enum LoginPhase
	{
		NotStarted,
		LoggingIn,
		LoggedIn,
		Failed,
		CredentialsMissing
	}

	enum BridgeStage
	{
		Entry,
		ArchiveSearch,
		ArchiveDetail
	}

	bool autoLoginArmed = true;
	bool diagOpen;
	bool startupLoginStarted;
	bool loginInProgress;
	int loginAttempts;
	LoginPhase loginPhase;
	string settledUrl = "";
	bool suppressLoginNavigation;
	int bridgeToken;
	string bridgeNo = "";
	bool bridgeResume;
	bool bridgeActive;
	int bridgeTimeoutToken;
	int bridgeTimeoutGeneration;
	Action<int, string, CustomerCard?>? bridgeCallback;
	BridgeStage bridgeStage;
	bool bridgeResumeConsumed;
	string bridgeArchiveUrl = "";
	int searchToken;
	bool searchActive;
	bool searchNeedsResume;
	bool searchResumedOnce;
	int searchTimeoutToken;
	string searchReception = "";
	string searchName = "";
	string searchTel = "";
	string searchPoint = "";
	string searchSlip = "";
	Action<int, string, IReadOnlyList<SearchHit>?>? searchCallback;
	int detailToken;
	bool detailActive;
	bool detailNeedsResume;
	bool detailResumedOnce;
	int detailTimeoutToken;
	string detailTarget = "";
	Action<int, string, CustomerCard?>? detailCallback;

	static readonly Color PageBg = Color.FromArgb("#EEF3F8");
	static readonly Color Ink = Color.FromArgb("#0F172A");
	static readonly Color Muted = Color.FromArgb("#64748B");
	static readonly Color BandBasic = Color.FromArgb("#1D6FA5");
	static readonly Color BodyBasic = Color.FromArgb("#F4F9FC");
	static readonly Color BandContact = Color.FromArgb("#1F8A70");
	static readonly Color BodyContact = Color.FromArgb("#F3FAF7");
	static readonly Color BandCharge = Color.FromArgb("#E08A1E");
	static readonly Color BodyCharge = Color.FromArgb("#FFF8ED");
	static readonly Color BandDesk = Color.FromArgb("#6D5BD0");
	static readonly Color BodyDesk = Color.FromArgb("#F7F5FC");
	static readonly Color BandVisit = Color.FromArgb("#1A8A9A");
	static readonly Color BodyVisit = Color.FromArgb("#F2FBFC");
	static readonly Color BandRequest = Color.FromArgb("#5C6B7A");
	static readonly Color BodyRequest = Color.FromArgb("#F6F8FA");
	static readonly Color BandCustomer = Color.FromArgb("#0E5A8A");
	static readonly Color BodyCustomer = Color.FromArgb("#F7FBFE");
	static readonly Color BandReply = Color.FromArgb("#9A3D6A");
	static readonly Color BodyReply = Color.FromArgb("#FDF6F9");
	static readonly Color BandProgress = Color.FromArgb("#3F6212");
	static readonly Color BodyProgress = Color.FromArgb("#F7FAF3");
	static readonly Color BandReport = Color.FromArgb("#0F6B4C");
	static readonly Color BodyReport = Color.FromArgb("#F3FAF6");

	public JSystemPage()
	{
		InitializeComponent();
		Current = this;
	}

	public void BeginStartupLogin()
	{
		if (startupLoginStarted) return;
		startupLoginStarted = true;
		_ = StartupLoginAsync();
	}

	public void OpenForManualLogin()
	{
		if (loginPhase == LoginPhase.LoggedIn || JudgeLogin(currentUrl) == "ログイン済みらしい")
		{
			loginPhase = LoginPhase.LoggedIn;
			SetSessionStatus("Jシステム：ログイン済み");
			FillStatusLabel.Text = "自動ログイン：待機";
			return;
		}
		loginAttempts = 0;
		loginInProgress = false;
		autoLoginArmed = true;
		loginPhase = LoginPhase.LoggingIn;
		SetSessionStatus("Jシステム：ログイン中...");
		OpenUrl(LoginUrl, "読み込み中");
	}

	async Task StartupLoginAsync()
	{
		try
		{
			var stored = await JSystemCredentialStore.LoadAsync();
			if (string.IsNullOrEmpty(stored.UserId) || string.IsNullOrEmpty(stored.Password))
			{
				loginPhase = LoginPhase.CredentialsMissing;
				SetSessionStatus("Jシステム：ログイン情報未設定");
				return;
			}
		if (loginPhase == LoginPhase.LoggedIn || JudgeLogin(currentUrl) == "ログイン済みらしい")
		{
			loginPhase = LoginPhase.LoggedIn;
			SetSessionStatus("Jシステム：ログイン済み");
			return;
		}
		if (loginInProgress || loginPhase == LoginPhase.LoggingIn) return;
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				EnsureWebViewHandler();
				loginPhase = LoginPhase.LoggingIn;
				SetSessionStatus("Jシステム：ログイン中...");
				autoLoginArmed = true;
				OpenUrl(LoginUrl, "読み込み中");
			});
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			loginPhase = LoginPhase.Failed;
			SetSessionStatus("Jシステム：ログイン失敗");
		}
	}

	void EnsureWebViewHandler()
	{
		if (JWeb.Handler != null)
		{
			ConfigureAndroidWebView();
			return;
		}
		try
		{
			var context = Handler?.MauiContext
				?? Window?.Handler?.MauiContext
				?? Application.Current?.Windows.FirstOrDefault()?.Handler?.MauiContext;
			if (context == null) return;
			_ = JWeb.ToPlatform(context);
			ConfigureAndroidWebView();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		ConfigureAndroidWebView();
		UpdateArea();
		if (loginPhase != LoginPhase.LoggedIn) return;
		suppressLoginNavigation = true;
		KeepLoggedInPage();
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => suppressLoginNavigation = false);
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		ConfigureAndroidWebView();
		if (loginPhase != LoginPhase.LoggedIn) return;
		suppressLoginNavigation = true;
		KeepLoggedInPage();
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => suppressLoginNavigation = false);
	}

	void ConfigureAndroidWebView()
	{
#if ANDROID
		if (JWeb.Handler?.PlatformView is not Android.Webkit.WebView native) return;
		native.SetBackgroundColor(Android.Graphics.Color.White);
		if (native.Settings == null) return;
		native.Settings.JavaScriptEnabled = true;
		native.Settings.DomStorageEnabled = true;
#endif
	}

	void OnOpenHomeClicked(object? sender, EventArgs e)
	{
		OpenUrl(HomeUrl, "読み込み中");
	}

	void OnOpenCustomerClicked(object? sender, EventArgs e)
	{
		var no = (ReceptionEntry.Text ?? "").Trim();
		if (!IsReceptionNo(no))
		{
			ResultLabel.Text = "受付番号は8桁で入力してください";
			return;
		}
		ResultLabel.Text = "顧客ページの読み込みを開始しました";
		OpenUrl(CustomerUrlPrefix + no, "顧客ページ読み込み中");
	}

	void OpenUrl(string url, string loadingState)
	{
		viewState = loadingState;
		LoadStateLabel.Text = "Jシステム状態：" + viewState;
		LoginStateLabel.Text = "ログイン状態：未確認";
		try
		{
			JWeb.Source = url;
		}
		catch (Exception ex)
		{
			ShowLoadFailure(SafeError(ex));
		}
	}

	void OnWebNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (suppressLoginNavigation && loginPhase == LoginPhase.LoggedIn && IsLoginUrl(e.Url))
		{
			e.Cancel = true;
			KeepLoggedInPage();
			return;
		}
		if (viewState is not ("顧客ページ読み込み中" or "検索ページ読み込み中" or "検索結果読み込み中" or "検索詳細読み込み中" or "過去検索読み込み中" or "過去検索結果読み込み中" or "過去案件読み込み中"))
			viewState = "読み込み中";
		LoadStateLabel.Text = "Jシステム状態：" + viewState;
	}

	void KeepLoggedInPage()
	{
		if (loginPhase != LoginPhase.LoggedIn) return;
		if (string.IsNullOrEmpty(settledUrl) || IsLoginUrl(settledUrl)) return;
		var sourceUrl = (JWeb.Source as UrlWebViewSource)?.Url ?? "";
		if (!IsLoginUrl(sourceUrl) && !IsLoginUrl(currentUrl)) return;
		if (string.Equals(sourceUrl, settledUrl, StringComparison.OrdinalIgnoreCase)) return;
		try
		{
			JWeb.Source = settledUrl;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
		}
	}

	static bool IsLoginUrl(string? url)
	{
		return JudgeLogin(url ?? "") == "ログイン画面";
	}

	void OnWebNavigated(object? sender, WebNavigatedEventArgs e)
	{
		currentUrl = e.Url ?? "";
		CurrentUrlLabel.Text = "現在URL：" + UrlPrivacy.ForDisplay(currentUrl);
		if (e.Result == WebNavigationResult.Success)
		{
			var customerReadPending = viewState == "顧客ページ読み込み中";
			var searchFormPending = viewState == "検索ページ読み込み中";
			var searchResultPending = viewState == "検索結果読み込み中";
			var detailPending = viewState == "検索詳細読み込み中";
			var archiveFormPending = viewState == "過去検索読み込み中";
			var archiveResultPending = viewState == "過去検索結果読み込み中";
			var archiveDetailPending = viewState == "過去案件読み込み中";
			viewState = "表示完了";
			LoadStateLabel.Text = "Jシステム状態：表示完了";
			var login = JudgeLogin(currentUrl);
			LoginStateLabel.Text = "ログイン状態：" + login;
			if (login == "ログイン済みらしい")
			{
				if (loginPhase == LoginPhase.LoggedIn && !IsLoginUrl(currentUrl))
					settledUrl = currentUrl;
				if (customerReadPending && bridgeActive && IsCustomerDetailPage(currentUrl)
					&& currentUrl.Contains("no=" + bridgeNo, StringComparison.Ordinal))
					_ = FinishBridgeReadAsync(bridgeToken, bridgeNo);
				else if (searchFormPending && searchActive && IsSearchReceptionPage(currentUrl))
					_ = SubmitReceptionSearchAsync();
				else if (searchResultPending && searchActive && IsSearchReceptionPage(currentUrl))
					_ = FinishReceptionSearchAsync();
				else if (detailPending && detailActive && IsSameHistoryDetail(currentUrl, detailTarget))
					_ = FinishHistoryDetailAsync();
				else if (archiveFormPending && bridgeActive && bridgeStage == BridgeStage.ArchiveSearch)
					_ = SubmitBridgeSearchAsync();
				else if (archiveResultPending && bridgeActive && bridgeStage == BridgeStage.ArchiveSearch)
					_ = FinishBridgeSearchAsync();
				else if (archiveDetailPending && bridgeActive && bridgeStage == BridgeStage.ArchiveDetail && IsSameHistoryDetail(currentUrl, bridgeArchiveUrl))
					_ = FinishBridgeHistoryAsync();
				else if (searchActive && searchNeedsResume)
					TryResumeSearch();
				else if (detailActive && detailNeedsResume)
					TryResumeDetail();
				else if (bridgeActive && bridgeResume)
					TryResumeBridge();
				else if (!searchActive && !detailActive && !bridgeActive)
					_ = ConfirmSessionAsync();
			}
			else if (login == "ログイン画面")
			{
				if (bridgeActive)
				{
					if (bridgeResumeConsumed) ReportBridge(bridgeToken, "fail", null);
					else bridgeResume = true;
				}
				if (searchActive)
				{
					if (searchResumedOnce) ReportSearch(searchToken, "fail", null);
					else searchNeedsResume = true;
				}
				if (detailActive)
				{
					if (detailResumedOnce) ReportDetail(detailToken, "fail", null);
					else detailNeedsResume = true;
				}
				if (loginPhase == LoginPhase.LoggedIn && suppressLoginNavigation)
				{
					KeepLoggedInPage();
					return;
				}
				if (loginPhase == LoginPhase.LoggedIn)
					loginAttempts = 0;
				loginPhase = LoginPhase.LoggingIn;
				QueueAutoLogin();
			}
			return;
		}
		ShowLoadFailure(DescribeNavigation(e.Result));
	}

	void ShowLoadFailure(string detail)
	{
		viewState = "読み込み失敗";
		LoadStateLabel.Text = "Jシステム状態：読み込み失敗";
		LoginStateLabel.Text = "ログイン状態：判定不能";
		ResultLabel.Text = "エラー内容：" + detail;
	}

	void OnWebSizeChanged(object? sender, EventArgs e)
	{
		UpdateArea();
	}

	void UpdateArea()
	{
		var width = Math.Max(0, (int)JWeb.Width);
		var height = Math.Max(0, (int)JWeb.Height);
		AreaLabel.Text = width > 0 && height > 0
			? "表示領域：" + width + " x " + height
			: "表示領域：0（WebViewに高さがありません）";
	}

	async void OnBasicJsClicked(object? sender, EventArgs e)
	{
		await ShowResultAsync("JS基本テスト：開始");
		await Task.Delay(100);
		var eval = await EvaluateAsync("1+1");
		if (eval.TimedOut)
		{
			await ShowResultAsync("JS基本テスト：タイムアウト");
			return;
		}
		if (eval.Failed || !TryReadLength(eval.Raw, out var number) || number != 2)
		{
			await ShowResultAsync("JS基本テスト：失敗\n例外：" + (eval.ExceptionName.Length == 0 ? "戻り値が2ではありません" : eval.ExceptionName));
			return;
		}
		await ShowResultAsync("JS基本テスト：成功\n結果：2");
	}

	async void OnDiagnoseClicked(object? sender, EventArgs e)
	{
		var lines = new List<string> { "JavaScript診断" };
		await ShowResultAsync("JavaScript診断\nSTEP1：実行中");
		lines.Add(await DiagnoseNumberAsync(1, "1+1", 2));
		await ShowResultAsync(string.Join("\n", lines) + "\nSTEP2：実行中");
		lines.Add(await DiagnoseTitleAsync());
		await ShowResultAsync(string.Join("\n", lines) + "\nSTEP3：実行中");
		lines.Add(await DiagnoseUrlAsync());
		await ShowResultAsync(string.Join("\n", lines) + "\nSTEP4：実行中");
		lines.Add(await DiagnoseCountAsync(4, "document.body?document.body.innerText.length:-1"));
		await ShowResultAsync(string.Join("\n", lines) + "\nSTEP5：実行中");
		lines.Add(await DiagnoseCountAsync(5, "document.documentElement?document.documentElement.outerHTML.length:-1"));
		await ShowResultAsync(string.Join("\n", lines));
	}

	async void OnReadHtmlClicked(object? sender, EventArgs e)
	{
		await ShowResultAsync("DOM読み取り：開始");
		await Task.Delay(100);
		await ShowResultAsync("DOM読み取り：実行中");
		var eval = await EvaluateAsync("document.documentElement?document.documentElement.outerHTML.length:-1");
		if (eval.TimedOut)
		{
			await ShowResultAsync("DOM読み取り：タイムアウト");
			return;
		}
		if (eval.Failed || !TryReadLength(eval.Raw, out var length) || length < 0)
		{
			await ShowResultAsync("DOM読み取り：失敗\n例外：" + (eval.ExceptionName.Length == 0 ? "戻り値が不正です" : eval.ExceptionName));
			return;
		}
		await ShowResultAsync("DOM読み取り：成功\nHTML文字数：" + length);
	}

	async void OnSurveyDomClicked(object? sender, EventArgs e)
	{
		await ShowResultAsync("DOM構造調査：開始");
		await Task.Delay(100);
		var eval = await EvaluateAsync(BuildDomSurveyScript());
		if (eval.TimedOut)
		{
			await ShowResultAsync("DOM構造調査：タイムアウト");
			return;
		}
		if (eval.Failed)
		{
			await ShowResultAsync("DOM構造調査：失敗\n失敗段階：WebView戻り値取得");
			return;
		}
		if (!TryFormatDomSurvey(eval.Raw, out var text, out var failure))
		{
			await ShowResultAsync("DOM構造調査：失敗\n失敗段階：" + failure);
			return;
		}
		await ShowResultAsync(text);
	}

	void OnToggleDiagClicked(object? sender, EventArgs e)
	{
		diagOpen = !diagOpen;
		DiagPanel.IsVisible = diagOpen;
		DiagToggleButton.Text = (diagOpen ? "▼" : "▶") + " 診断 / 開発者向け";
	}

	static bool IsCustomerDetailPage(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		return uri.AbsolutePath.Contains("/pc_support/trader/visit/entry_disp_sub.php", StringComparison.OrdinalIgnoreCase);
	}

	static string BuildCustomerReadScript()
	{
		return "(function(){" +
			"function normalize(raw,keepBreaks){" +
			"var t=String(raw==null?'':raw).replace(/\\u00a0/g,' ');" +
			"if(keepBreaks){" +
			"t=t.replace(/\\r\\n/g,'\\n').replace(/\\r/g,'\\n');" +
			"var lines=t.split('\\n');" +
			"for(var i=0;i<lines.length;i++) lines[i]=lines[i].replace(/[ \\t\\f\\v]+/g,' ').replace(/^ +/,'').replace(/ +$/,'');" +
			"return lines.join('\\n').replace(/^\\n+/,'').replace(/\\n+$/,'').replace(/\\n{3,}/g,'\\n\\n');}" +
			"return t.replace(/\\s+/g,' ').replace(/^ +/,'').replace(/ +$/,'');}" +
			"function labelOf(el){return normalize(el.textContent,false);}" +
			"function cellsAfter(th){" +
			"var cells=[];var n=th.nextElementSibling;" +
			"while(n){var tag=(n.tagName||'').toUpperCase();if(tag==='TH')break;if(tag==='TD')cells.push(n);n=n.nextElementSibling;}" +
			"if(cells.length)return cells;" +
			"var row=th.parentElement;if(!row)return cells;var kids=row.children;var seen=false;" +
			"for(var i=0;i<kids.length;i++){if(kids[i]===th){seen=true;continue;}if(!seen)continue;var tag2=(kids[i].tagName||'').toUpperCase();if(tag2==='TH')break;if(tag2==='TD')cells.push(kids[i]);}" +
			"return cells;}" +
			"function sameLabel(el,name){var t=labelOf(el).replace(/[：:]\\s*$/,'');return t===name;}" +
			"function findTh(name){var ths=document.querySelectorAll('th');for(var i=0;i<ths.length;i++){if(sameLabel(ths[i],name))return ths[i];}return null;}" +
			"function findLabel(name){var th=findTh(name);if(th)return th;var nodes=document.querySelectorAll('td.infoHead,.infoHead');for(var i=0;i<nodes.length;i++){if(sameLabel(nodes[i],name))return nodes[i];}return null;}" +
			"function field(name,keepBreaks){var th=findLabel(name);if(!th)return '';var cells=cellsAfter(th);if(!cells.length)return '';return normalize(cells[0].textContent,!!keepBreaks);}" +
			"function pair(name){var th=findLabel(name);var a='';var b='';if(th){var cells=cellsAfter(th);if(cells.length>0)a=normalize(cells[0].textContent,false);if(cells.length>1)b=normalize(cells[1].textContent,false);}return {code:a,name:b};}" +
			"function money(v){var t=normalize(v,false);if(!t||t==='---')return t;return t.replace(/,/g,'');}" +
			"function at(list,index){if(index==null||index<0||index>=list.length)return '';return list[index];}" +
			"function chargesOf(){" +
			"var rows=[];var total='';var th=findTh('料金明細');if(!th)return {rows:rows,total:total};" +
			"var cells=cellsAfter(th);if(!cells.length)return {rows:rows,total:total};" +
			"var table=cells[0].querySelector('table');if(!table)return {rows:rows,total:total};" +
			"var trs=table.querySelectorAll('tr');var col=null;var header=-1;" +
			"for(var r=0;r<trs.length;r++){" +
			"var heads=trs[r].querySelectorAll('th,td');var labels=[];" +
			"for(var c=0;c<heads.length;c++) labels.push(normalize(heads[c].textContent,false));" +
			"if(labels.indexOf('商品コード')>=0&&labels.indexOf('商品名')>=0){" +
			"col={productCode:labels.indexOf('商品コード'),productName:labels.indexOf('商品名'),unitPrice:labels.indexOf('単価'),quantity:labels.indexOf('数量'),amount:labels.indexOf('金額'),billingType:labels.indexOf('依頼')};" +
			"header=r;break;}}" +
			"if(!col)return {rows:rows,total:total};" +
			"for(var r2=header+1;r2<trs.length;r2++){" +
			"var tds=trs[r2].querySelectorAll('td,th');var texts=[];" +
			"for(var k=0;k<tds.length;k++) texts.push(normalize(tds[k].textContent,false));" +
			"var isTotal=false;for(var t=0;t<texts.length;t++){if(texts[t]==='合計'||texts[t].indexOf('合計')===0)isTotal=true;}" +
			"if(isTotal){var amount=at(texts,col.amount);if(!amount){for(var u=texts.length-1;u>=0;u--){if(texts[u]&&texts[u]!=='合計'){amount=texts[u];break;}}}total=money(amount);continue;}" +
			"var item={productCode:at(texts,col.productCode),productName:at(texts,col.productName),unitPrice:money(at(texts,col.unitPrice)),quantity:money(at(texts,col.quantity)),amount:money(at(texts,col.amount)),billingType:at(texts,col.billingType)};" +
			"if(!item.productCode&&!item.productName&&!item.unitPrice&&!item.quantity&&!item.amount&&!item.billingType)continue;" +
			"rows.push(item);}" +
			"return {rows:rows,total:total,totalQty:''};}" +
			"function chargesForPage(){var path=String(location.pathname||'');if(path.indexOf('history_request_sub.php')>=0)return historyCharges();return chargesOf();}" +
			"function colIndex(labels,names){var n;var i;for(n=0;n<names.length;n++){var exact=labels.indexOf(names[n]);if(exact>=0)return exact;}for(n=0;n<names.length;n++){if(names[n].length<2)continue;for(i=0;i<labels.length;i++){if(labels[i].indexOf(names[n])>=0)return i;}}return -1;}" +
			"function historyCharges(){" +
			"var rows=[];var total='';var totalQty='';var root=document.getElementById('detailList');if(!root)return {rows:rows,total:total,totalQty:totalQty};" +
			"var table=root.querySelector('table.list')||root.querySelector('table');if(!table)return {rows:rows,total:total,totalQty:totalQty};" +
			"var headRow=table.querySelector('thead tr');if(!headRow){var probe=table.querySelectorAll('tr');for(var h=0;h<probe.length;h++){if(probe[h].querySelectorAll('th').length>=3){headRow=probe[h];break;}}}" +
			"if(!headRow)return {rows:rows,total:total,totalQty:totalQty};" +
			"var heads=headRow.querySelectorAll('th,td');var labels=[];for(var c=0;c<heads.length;c++)labels.push(normalize(heads[c].textContent,false));" +
			"var col={productCode:colIndex(labels,['商品コード']),productName:colIndex(labels,['型番/商品名','型番／商品名','商品名','型番']),unitPrice:colIndex(labels,['単価']),quantity:colIndex(labels,['数量']),amount:colIndex(labels,['金額']),billingType:colIndex(labels,['依頼','請求区分']),note:colIndex(labels,['備考'])};" +
			"var body=table.querySelectorAll('tbody tr.dataline');if(!body.length)body=table.querySelectorAll('tbody tr');" +
			"for(var r=0;r<body.length;r++){if(body[r].querySelector('th'))continue;var tds=body[r].querySelectorAll('td');var texts=[];for(var k=0;k<tds.length;k++)texts.push(normalize(tds[k].textContent,false));var joined=texts.join('');if(joined.indexOf('合計')>=0)continue;" +
			"var item={productCode:at(texts,col.productCode),productName:at(texts,col.productName),unitPrice:money(at(texts,col.unitPrice)),quantity:money(at(texts,col.quantity)),amount:money(at(texts,col.amount)),billingType:at(texts,col.billingType),note:at(texts,col.note)};" +
			"if(!item.productCode&&!item.productName&&!item.unitPrice&&!item.quantity&&!item.amount&&!item.billingType)continue;rows.push(item);}" +
			"var foot=table.querySelectorAll('tfoot th,tfoot td');var ftexts=[];for(var f=0;f<foot.length;f++)ftexts.push(normalize(foot[f].textContent,false));" +
			"total=money(at(ftexts,col.amount));totalQty=at(ftexts,col.quantity);if(totalQty==='数量'||totalQty==='合計')totalQty='';" +
			"if(!total){for(var u=ftexts.length-1;u>=0;u--){var candidate=money(ftexts[u]);if(candidate&&candidate!=='合計'){total=candidate;break;}}}" +
			"return {rows:rows,total:total,totalQty:totalQty};}" +
			"function clipText(value,max){var s=String(value||'');return s.length>max?s.substring(0,max):s;}" +
			"function findMarked(word){var titles=document.querySelectorAll('span.frmSTitle');for(var i=0;i<titles.length;i++){var title=normalize(titles[i].textContent,false);if(title.indexOf(word)>=0&&title.length<=40)return titles[i];}var nodes=document.querySelectorAll('caption,th');for(var j=0;j<nodes.length;j++){var t=normalize(nodes[j].textContent,false);if(t.indexOf(word)>=0&&t.length<=40)return nodes[j];}return null;}" +
			"function tableAfter(node){var current=node;for(var depth=0;depth<6&&current;depth++){var sib=current.nextElementSibling;while(sib){var tag=(sib.tagName||'').toUpperCase();if(tag==='TABLE')return sib;if(sib.querySelector){var inner=sib.querySelector('table.list')||sib.querySelector('table');if(inner)return inner;}sib=sib.nextElementSibling;}current=current.parentElement;}return null;}" +
			"function responsesOf(table){var rows=table.querySelectorAll('tr');var items=[];var current=null;function push(){if(current)items.push(current);current=null;}for(var r=0;r<rows.length&&items.length<30;r++){var tds=rows[r].querySelectorAll('td');if(!tds.length)continue;var hasAnchor=false;for(var i=0;i<tds.length;i++){if(parseInt(tds[i].getAttribute('rowspan')||'1',10)>=3)hasAnchor=true;}if(hasAnchor){push();var metas=[];var bodies=[];for(var j=0;j<tds.length;j++){var span=parseInt(tds[j].getAttribute('rowspan')||'1',10);var text=clipText(normalize(tds[j].textContent,true),1500);if(span>=3)metas.push(clipText(text,400));else bodies.push(text);}current={metas:metas,left:bodies.length?bodies[0]:'',right:bodies.length>1?bodies.slice(1).join('\\n'):''};}else if(current){var extra=[];for(var k=0;k<tds.length;k++)extra.push(clipText(normalize(tds[k].textContent,true),1500));if(extra.length>=2){if(extra[0])current.left=(current.left?current.left+'\\n':'')+extra[0];var rest=extra.slice(1).filter(Boolean).join('\\n');if(rest)current.right=(current.right?current.right+'\\n':'')+rest;}else if(extra.length===1&&extra[0])current.left=(current.left?current.left+'\\n':'')+extra[0];}}push();return items;}" +
			"function labeledValue(table,names){var nodes=table.querySelectorAll('th,td');for(var i=0;i<nodes.length;i++){var label=normalize(nodes[i].textContent,false).replace(/[：:]\\s*$/,'');for(var n=0;n<names.length;n++){if(label===names[n]){var next=nodes[i].nextElementSibling;return next?normalize(next.textContent,false):'';}}}return '';}" +
			"function isAdminRow(texts){var keys={'完了日':1,'担当者':1,'センター確認日':1,'センター確認':1,'確認':1,'確認OK':1,'確認ＮＧ':1};var meaningful=0;for(var i=0;i<texts.length;i++){var t=texts[i];if(!t)continue;if(keys[t])continue;if(t.length>40)return false;meaningful++;}return meaningful<=2;}" +
			"function progressOf(table){var report='';var items=[];var confirmResult='';var rows=table.querySelectorAll('tr');for(var r=0;r<rows.length;r++){var cells=rows[r].querySelectorAll('td,th');var texts=[];var reportCell='';for(var c=0;c<cells.length;c++){var t=normalize(cells[c].textContent,true);texts.push(t);var flat=normalize(cells[c].textContent,false);if(!confirmResult&&(flat==='確認OK'||flat==='確認ＮＧ'||flat.indexOf('確認OK')===0))confirmResult=flat;if(t.indexOf('◇完了報告◇')>=0)reportCell=t;}if(reportCell){report=clipText(reportCell,8000);continue;}if(isAdminRow(texts))continue;var parts=[];for(var p=0;p<texts.length;p++)if(texts[p])parts.push(texts[p]);if(!parts.length)continue;var body=clipText(parts.join('\\n'),2000);if(body.length<2)continue;if(items.length<20)items.push({text:body});}return {report:report,items:items,confirmResult:confirmResult};}" +
			"function readExtra(){var responses=[];var progress=[];var report='';var completedDate='';var worker='';var centerConfirmed='';var confirmResult='';var respTitle=findMarked('応答履歴');var respTable=respTitle?tableAfter(respTitle):null;if(!respTable){var pop=document.getElementById('hisListPop')||document.getElementById('reportHis');if(pop)respTable=pop.querySelector('table.list')||pop.querySelector('table');}if(respTable)responses=responsesOf(respTable);var progTitle=findMarked('経過履歴');var progTable=progTitle?tableAfter(progTitle):null;if(progTable){completedDate=labeledValue(progTable,['完了日']);worker=labeledValue(progTable,['担当者']);centerConfirmed=labeledValue(progTable,['センター確認日','センター確認']);var parsed=progressOf(progTable);report=parsed.report;progress=parsed.items;confirmResult=parsed.confirmResult;}if(!report){var cells=document.querySelectorAll('td,th');for(var i=0;i<cells.length&&i<500;i++){var tx=normalize(cells[i].textContent,true);if(tx.indexOf('◇完了報告◇')>=0){report=clipText(tx,8000);break;}}}return {responses:responses,progress:progress,report:report,completedDate:completedDate,worker:worker,centerConfirmed:centerConfirmed,confirmResult:confirmResult};}" +
			"var store=pair('受付店');var person=pair('受付者名');var charges=chargesForPage();var extra=readExtra();" +
			"var directText='';" +
			"for(var di=0;di<charges.rows.length;di++){var cr=charges.rows[di];directText+=(cr.productName||'')+(cr.productCode||'')+(cr.billingType||'')+(cr.unitPrice||'')+(cr.quantity||'')+(cr.amount||'');}" +
			"var directTh=findTh('料金明細');if(directTh){var directCells=cellsAfter(directTh);if(directCells.length)directText+=(directCells[0].textContent||'');}" +
			"directText=directText.replace(/\\s+/g,'');" +
			"var hasDirect=directText.indexOf('直集')>=0||directText.indexOf('直収')>=0;" +
			"return encodeURIComponent(JSON.stringify({" +
			"receptionNo:field('受付No',false),slipNo:field('伝票番号',false)," +
			"storeCode:store.code,storeName:store.name,receptionistCode:person.code,receptionistName:person.name," +
			"customerName:field('お客様名',false),furigana:field('フリガナ',false),address:field('住所',true)," +
			"phone1:field('電話①',false),phone2:field('電話②',false),visitConfirmed:field('訪問確定',false),worker:field('作業担当',false)," +
			"visitRequested:field('訪問希望',false),requestUseField:field('依頼先使用欄',true),requestDate:field('依頼日',false),visitScheduled:field('訪問予定',false)," +
			"requestDetails:field('依頼内容',true),promiseDetails:field('約束事項',true),requestSupplement:field('依頼補足',true),promiseSupplement:field('約束補足',true)," +
			"contractNo:field('契約書No',false),charges:charges.rows,chargeTotal:charges.total,chargeQuantityTotal:charges.totalQty||'',hasDirectCollection:hasDirect," +
			"responses:extra.responses,progress:extra.progress,completionReport:extra.report,completionDate:extra.completedDate,completionWorker:extra.worker,centerConfirmed:extra.centerConfirmed,confirmResult:extra.confirmResult" +
			"}));})()";
	}

	static string BuildDomSurveyScript()
	{
		return "(function(){" +
			"var limit=200;" +
			"var nodes=document.querySelectorAll('input,textarea,select,button,label,th,td');" +
			"var out=[];" +
			"function attr(el,name){var v=el.getAttribute(name);return v?String(v):'';}" +
			"function clip(v,max){v=String(v||'');if(v.length>max)v=v.substring(0,max);return v;}" +
			"function mask(raw){" +
			"var t=String(raw||'').replace(/[０-９]/g,function(ch){return String.fromCharCode(ch.charCodeAt(0)-0xFEE0);}).replace(/[－ー―−]/g,'-').replace(/＠/g,'@').replace(/\\s+/g,' ').trim();" +
			"if(!t)return '';" +
			"t=t.replace(/[A-Za-z0-9._%+\\-]+@[A-Za-z0-9.\\-]+\\.[A-Za-z]{2,}/gi,'[EMAIL]');" +
			"t=t.replace(/\\d{2,4}-\\d{2,4}-\\d{3,4}/g,'[PHONE]');" +
			"t=t.replace(/\\d{7,}/g,'[NUMBER]');" +
			"if(t.length>40)t=t.substring(0,40);" +
			"return t;}" +
			"var count=Math.min(nodes.length,limit);" +
			"for(var i=0;i<count;i++){" +
			"var el=nodes[i];" +
			"var tag=(el.tagName||'').toUpperCase();" +
			"var item={tag:tag};" +
			"var id=clip(el.id||'',80);" +
			"var name=clip(attr(el,'name'),80);" +
			"var cls=el.className;" +
			"if(typeof cls!=='string')cls=attr(el,'class');" +
			"cls=clip(cls,120);" +
			"var type=clip(attr(el,'type'),40);" +
			"var forAttr=clip(attr(el,'for'),80);" +
			"var placeholder=mask(attr(el,'placeholder'));" +
			"if(id)item.id=id;" +
			"if(name)item.name=name;" +
			"if(cls)item['class']=cls;" +
			"if(type)item.type=type;" +
			"if(forAttr)item['for']=forAttr;" +
			"if(placeholder)item.placeholder=placeholder;" +
			"if(tag==='LABEL'||tag==='TH'||tag==='TD'){" +
			"var text=mask(el.textContent||'');" +
			"if(text)item.text=text;}" +
			"out.push(item);}" +
			"if(nodes.length>limit)out.push({truncated:true,total:nodes.length});" +
			"return encodeURIComponent(JSON.stringify(out)));})()";
	}

	static bool TryFormatDomSurvey(string? raw, out string text, out string failure)
	{
		text = "";
		if (!TryReadEncodedJson(raw, JsonValueKind.Array, out var document, out failure) || document == null) return false;
		using (document)
		{
			try
			{
				text = FormatSurveyArray(document.RootElement);
				return true;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				failure = "画面表示";
				return false;
			}
		}
	}

	static bool TryBindCustomer(string? raw, out CustomerCard? card)
	{
		card = null;
		if (!TryReadEncodedJson(raw, JsonValueKind.Object, out var document, out _) || document == null) return false;
		using (document)
		{
			try
			{
				card = BindCustomer(document.RootElement);
				return true;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				return false;
			}
		}
	}

	static CustomerCard BindCustomer(JsonElement obj)
	{
		var card = new CustomerCard
		{
			ReceptionNo = ReadSurveyString(obj, "receptionNo"),
			SlipNo = ReadSurveyString(obj, "slipNo"),
			StoreCode = ReadSurveyString(obj, "storeCode"),
			StoreName = ReadSurveyString(obj, "storeName"),
			ReceptionistCode = ReadSurveyString(obj, "receptionistCode"),
			ReceptionistName = ReadSurveyString(obj, "receptionistName"),
			CustomerName = ReadSurveyString(obj, "customerName"),
			Furigana = ReadSurveyString(obj, "furigana"),
			Address = ReadSurveyString(obj, "address"),
			Phone1 = ReadSurveyString(obj, "phone1"),
			Phone2 = ReadSurveyString(obj, "phone2"),
			VisitConfirmed = ReadSurveyString(obj, "visitConfirmed"),
			Worker = ReadSurveyString(obj, "worker"),
			VisitRequested = ReadSurveyString(obj, "visitRequested"),
			RequestUseField = ReadSurveyString(obj, "requestUseField"),
			RequestDate = ReadSurveyString(obj, "requestDate"),
			VisitScheduled = ReadSurveyString(obj, "visitScheduled"),
			RequestDetails = ReadSurveyString(obj, "requestDetails"),
			PromiseDetails = ReadSurveyString(obj, "promiseDetails"),
			RequestSupplement = ReadSurveyString(obj, "requestSupplement"),
			PromiseSupplement = ReadSurveyString(obj, "promiseSupplement"),
			ContractNo = ReadSurveyString(obj, "contractNo"),
			ChargeTotal = ReadSurveyString(obj, "chargeTotal"),
			ChargeQuantityTotal = ReadSurveyString(obj, "chargeQuantityTotal"),
			CompletionReport = ReadSurveyString(obj, "completionReport"),
			CompletionDate = ReadSurveyString(obj, "completionDate"),
			CompletionWorker = ReadSurveyString(obj, "completionWorker"),
			CenterConfirmed = ReadSurveyString(obj, "centerConfirmed"),
			ConfirmResult = ReadSurveyString(obj, "confirmResult"),
			HasDirectCollection = obj.TryGetProperty("hasDirectCollection", out var direct) && direct.ValueKind == JsonValueKind.True
		};
		if (obj.TryGetProperty("charges", out var charges) && charges.ValueKind == JsonValueKind.Array)
		{
			foreach (var row in charges.EnumerateArray())
			{
				if (row.ValueKind != JsonValueKind.Object) continue;
				card.Charges.Add(new ChargeCard
				{
					ProductCode = ReadSurveyString(row, "productCode"),
					ProductName = ReadSurveyString(row, "productName"),
					UnitPrice = ReadSurveyString(row, "unitPrice"),
					Quantity = ReadSurveyString(row, "quantity"),
					Amount = ReadSurveyString(row, "amount"),
					BillingType = ReadSurveyString(row, "billingType"),
					Note = ReadSurveyString(row, "note")
				});
			}
		}
		if (obj.TryGetProperty("responses", out var responses) && responses.ValueKind == JsonValueKind.Array)
		{
			foreach (var row in responses.EnumerateArray())
			{
				if (row.ValueKind != JsonValueKind.Object) continue;
				card.Responses.Add(BindResponse(row));
			}
		}
		if (obj.TryGetProperty("progress", out var progress) && progress.ValueKind == JsonValueKind.Array)
		{
			foreach (var row in progress.EnumerateArray())
			{
				if (row.ValueKind != JsonValueKind.Object) continue;
				var text = ReadSurveyString(row, "text");
				if (!HasDisplayValue(text)) continue;
				card.Progress.Add(new ProgressCard { Text = text });
			}
		}
		return card;
	}

	static ResponseCard BindResponse(JsonElement row)
	{
		SplitResponse(ReadStringList(row, "metas"), out var at, out var party, out var kind);
		return new ResponseCard
		{
			At = at,
			Party = party,
			Kind = kind,
			Left = ReadSurveyString(row, "left"),
			Right = ReadSurveyString(row, "right")
		};
	}

	static void SplitResponse(IReadOnlyList<string> metas, out string at, out string party, out string kind)
	{
		at = "";
		kind = "";
		var names = new List<string>();
		foreach (var meta in metas)
		{
			foreach (var raw in meta.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0) continue;
				if (at.Length == 0 && Regex.IsMatch(line, @"\d{4}[./]\d{1,2}[./]\d{1,2}"))
				{
					at = line;
					continue;
				}
				if (line is "受信" or "発信" or "送信" or "着信")
				{
					kind = line;
					continue;
				}
				names.Add(line);
			}
		}
		party = string.Join(" → ", names);
	}

	static List<string> ReadStringList(JsonElement item, string name)
	{
		var list = new List<string>();
		if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return list;
		foreach (var entry in value.EnumerateArray())
		{
			if (entry.ValueKind == JsonValueKind.String) list.Add(entry.GetString() ?? "");
		}
		return list;
	}

	static bool TryReadEncodedJson(string? raw, JsonValueKind expected, out JsonDocument? document, out string failure)
	{
		document = null;
		failure = "WebView戻り値取得";
		if (raw == null) return false;
		var text = raw.Trim().TrimStart('\uFEFF');
		if (text.Length == 0 || IsSurveyNullToken(text) || IsSurveyUndefinedToken(text)) return false;
		if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
		{
			if (!TryDecodeSurveyJsonString(text, out var inner))
			{
				failure = "外側文字列デコード";
				return false;
			}
			text = inner.Trim();
			if (text.Length == 0 || IsSurveyNullToken(text) || IsSurveyUndefinedToken(text)) return false;
		}
		string decoded;
		try
		{
			decoded = Uri.UnescapeDataString(text);
		}
		catch (UriFormatException)
		{
			failure = "URIデコード";
			return false;
		}
		if (decoded.Length == 0)
		{
			failure = "URIデコード";
			return false;
		}
		try
		{
			var doc = JsonDocument.Parse(decoded);
			if (doc.RootElement.ValueKind != expected)
			{
				doc.Dispose();
				failure = "JSON解析";
				return false;
			}
			document = doc;
			failure = "";
			return true;
		}
		catch (JsonException)
		{
			failure = "JSON解析";
			return false;
		}
	}

	static bool TryDecodeSurveyJsonString(string text, out string inner)
	{
		inner = "";
		try
		{
			var decoded = JsonSerializer.Deserialize<string>(text);
			if (decoded == null) return false;
			inner = decoded;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	static bool IsSurveyNullToken(string text)
	{
		return text.Equals("null", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("\"null\"", StringComparison.OrdinalIgnoreCase);
	}

	static bool IsSurveyUndefinedToken(string text)
	{
		return text.Equals("undefined", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("\"undefined\"", StringComparison.OrdinalIgnoreCase);
	}

	static string FormatSurveyArray(JsonElement array)
	{
		var body = new StringBuilder();
		var index = 0;
		var truncated = false;
		var total = 0;
		foreach (var item in array.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object) continue;
			if (item.TryGetProperty("truncated", out var flag) && flag.ValueKind == JsonValueKind.True)
			{
				truncated = true;
				if (item.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var detected)) total = detected;
				continue;
			}
			index++;
			body.AppendLine();
			body.Append('[').Append(index).AppendLine("]");
			AppendSurveyLine(body, "TAG", ReadSurveyString(item, "tag"));
			AppendSurveyLine(body, "id", ReadSurveyString(item, "id"));
			AppendSurveyLine(body, "name", ReadSurveyString(item, "name"));
			AppendSurveyLine(body, "class", ReadSurveyString(item, "class"));
			AppendSurveyLine(body, "type", ReadSurveyString(item, "type"));
			AppendSurveyLine(body, "for", ReadSurveyString(item, "for"));
			AppendSurveyLine(body, "placeholder", RedactProbeText(ReadSurveyString(item, "placeholder")));
			AppendSurveyLine(body, "text", RedactProbeText(ReadSurveyString(item, "text")));
		}
		var header = new StringBuilder();
		header.Append("DOM構造調査：成功\n要素数：").Append(index);
		if (truncated)
		{
			header.Append("\n打ち切り：あり");
			if (total > 0) header.Append("\n検出数：").Append(total);
		}
		return header + body.ToString();
	}

	void AddCustomerSections(Layout stack, CustomerCard card)
	{
		var priority = card.PromiseDetails;
		stack.Children.Add(MakeBandCard("基本情報", BandBasic, BodyBasic,
			MakePair("受付No", card.ReceptionNo, "伝票番号", card.SlipNo),
			MakePlainField("訪問確定", card.VisitConfirmed, 17, true)));
		stack.Children.Add(MakeSummaryCard(card));
		stack.Children.Add(MakeBandCard("連絡先", BandContact, BodyContact,
			MakeAddressRow(card.Address),
			MakeActionRow("電話①", card.Phone1, IsPriorityPhone(card.Phone1, priority)),
			MakeActionRow("電話②", card.Phone2, IsPriorityPhone(card.Phone2, priority))));
		stack.Children.Add(MakeChargeSection(card));
		stack.Children.Add(MakeBandCard("受付情報", BandDesk, BodyDesk,
			MakePlainField("受付店", JoinDisplay(card.StoreCode, card.StoreName)),
			MakePlainField("受付者", JoinDisplay(card.ReceptionistCode, card.ReceptionistName))));
		stack.Children.Add(MakeBandCard("訪問情報", BandVisit, BodyVisit,
			MakePlainField("作業担当", card.Worker),
			MakePlainField("訪問希望", card.VisitRequested),
			MakePlainField("依頼日", card.RequestDate),
			MakePlainField("訪問予定", card.VisitScheduled)));
		stack.Children.Add(MakeBandCard("依頼情報", BandRequest, BodyRequest,
			MakeLongField("依頼先使用欄", card.RequestUseField),
			MakeLongField("依頼内容", card.RequestDetails),
			MakeLongField("約束事項", card.PromiseDetails),
			MakeLongField("依頼補足", card.RequestSupplement),
			MakeLongField("約束補足", card.PromiseSupplement)));
		if (card.Responses.Count > 0) stack.Children.Add(MakeResponseSection(card));
		if (card.Progress.Count > 0) stack.Children.Add(MakeProgressSection(card));
		if (HasCompletion(card)) stack.Children.Add(MakeCompletionSection(card));
	}

	static string JoinDisplay(string code, string name)
	{
		var left = DisplayValue(code);
		var right = DisplayValue(name);
		if (left == "---") return right;
		if (right == "---") return left;
		return left + " " + right;
	}

	View MakeSummaryCard(CustomerCard card)
	{
		var body = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12, 10) };
		if (card.HasDirectCollection)
		{
			body.Children.Add(new Border
			{
				BackgroundColor = Color.FromArgb("#DC2626"),
				StrokeThickness = 0,
				Padding = new Thickness(8, 3),
				HorizontalOptions = LayoutOptions.End,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
				Content = new Label
				{
					Text = "直集案件",
					FontSize = 12,
					FontAttributes = FontAttributes.Bold,
					TextColor = Colors.White
				}
			});
		}
		var names = new FlexLayout
		{
			Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
			Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
			AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
			JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start
		};
		names.Children.Add(new Label
		{
			Text = DisplayNameWithHonorific(card.CustomerName),
			FontSize = 22,
			FontAttributes = FontAttributes.Bold,
			TextColor = Ink,
			LineBreakMode = LineBreakMode.WordWrap,
			Margin = new Thickness(0, 0, 8, 0)
		});
		if (HasDisplayValue(card.Furigana))
		{
			names.Children.Add(new Label
			{
				Text = DisplayValue(card.Furigana),
				FontSize = 15,
				TextColor = Muted,
				LineBreakMode = LineBreakMode.WordWrap,
				VerticalOptions = LayoutOptions.Center
			});
		}
		body.Children.Add(names);
		body.BackgroundColor = BodyCustomer;
		var stack = new VerticalStackLayout { Spacing = 0, BackgroundColor = BodyCustomer };
		stack.Children.Add(new Label
		{
			Text = "お客様情報",
			FontSize = 14,
			FontAttributes = FontAttributes.Bold,
			TextColor = Colors.White,
			BackgroundColor = BandCustomer,
			Padding = new Thickness(10, 5),
			HorizontalOptions = LayoutOptions.Fill
		});
		stack.Children.Add(body);
		return new Border
		{
			BackgroundColor = BodyCustomer,
			Stroke = BandCustomer,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = 0,
			Content = stack
		};
	}

	static bool HasCompletion(CustomerCard card)
	{
		return HasDisplayValue(card.CompletionReport)
			|| HasDisplayValue(card.CompletionDate)
			|| HasDisplayValue(card.CompletionWorker)
			|| HasDisplayValue(card.CenterConfirmed)
			|| HasDisplayValue(card.ConfirmResult);
	}

	View MakeResponseSection(CustomerCard card)
	{
		var content = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(8, 6), BackgroundColor = BodyReply };
		foreach (var item in card.Responses) content.Children.Add(MakeResponseCard(item));
		return MakeTitledBand("【応答履歴】", BandReply, BodyReply, content);
	}

	static View MakeResponseCard(ResponseCard item)
	{
		var body = new VerticalStackLayout { Spacing = 2 };
		var head = new HorizontalStackLayout { Spacing = 8 };
		if (HasDisplayValue(item.At))
		{
			head.Children.Add(new Label
			{
				Text = item.At.Trim(),
				FontSize = 14,
				FontAttributes = FontAttributes.Bold,
				TextColor = Ink,
				VerticalOptions = LayoutOptions.Center
			});
		}
		if (HasDisplayValue(item.Kind))
		{
			head.Children.Add(new Label
			{
				Text = item.Kind.Trim(),
				FontSize = 12,
				FontAttributes = FontAttributes.Bold,
				TextColor = BandReply,
				VerticalOptions = LayoutOptions.Center
			});
		}
		if (head.Children.Count > 0) body.Children.Add(head);
		if (HasDisplayValue(item.Party))
		{
			body.Children.Add(new Label
			{
				Text = item.Party.Trim(),
				FontSize = 13,
				TextColor = Muted,
				LineBreakMode = LineBreakMode.WordWrap
			});
		}
		if (HasDisplayValue(item.Left))
		{
			body.Children.Add(new Label
			{
				Text = item.Left.Trim(),
				FontSize = 15,
				TextColor = Ink,
				LineBreakMode = LineBreakMode.WordWrap
			});
		}
		if (HasDisplayValue(item.Right))
		{
			body.Children.Add(new Label { Text = "↓", FontSize = 13, TextColor = Muted });
			body.Children.Add(new Label
			{
				Text = item.Right.Trim(),
				FontSize = 15,
				TextColor = Ink,
				LineBreakMode = LineBreakMode.WordWrap
			});
		}
		return new Border
		{
			BackgroundColor = Colors.White,
			Stroke = Color.FromArgb("#E7C3D4"),
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Padding = new Thickness(8, 6),
			Content = body
		};
	}

	View MakeProgressSection(CustomerCard card)
	{
		var content = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(8, 6), BackgroundColor = BodyProgress };
		foreach (var item in card.Progress)
		{
			content.Children.Add(new Border
			{
				BackgroundColor = Colors.White,
				Stroke = Color.FromArgb("#D5E3B8"),
				StrokeThickness = 1,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
				Padding = new Thickness(8, 6),
				Content = new Label
				{
					Text = item.Text.Trim(),
					FontSize = 15,
					TextColor = Ink,
					LineBreakMode = LineBreakMode.WordWrap
				}
			});
		}
		return MakeTitledBand("【経過履歴】", BandProgress, BodyProgress, content);
	}

	View MakeCompletionSection(CustomerCard card)
	{
		var content = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(10, 6, 10, 8), BackgroundColor = BodyReport };
		if (HasDisplayValue(card.CompletionDate)) content.Children.Add(MakePlainField("完了日", card.CompletionDate));
		if (HasDisplayValue(card.CompletionWorker)) content.Children.Add(MakePlainField("担当者", card.CompletionWorker));
		if (HasDisplayValue(card.CenterConfirmed)) content.Children.Add(MakePlainField("センター確認日", card.CenterConfirmed));
		if (HasDisplayValue(card.ConfirmResult)) content.Children.Add(MakePlainField("確認", card.ConfirmResult));
		if (HasDisplayValue(card.CompletionReport)) content.Children.Add(FormatReport(card.CompletionReport));
		return MakeTitledBand("【完了報告】", BandReport, BodyReport, content);
	}

	static View FormatReport(string text)
	{
		var stack = new VerticalStackLayout { Spacing = 1 };
		var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		foreach (var line in normalized.Split('\n'))
		{
			var match = Regex.Match(line, @"^(■[^:：\n]{1,30})([:：])(.*)$");
			if (match.Success)
			{
				var row = new FlexLayout
				{
					Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
					Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
					AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start
				};
				row.Children.Add(new Label
				{
					Text = match.Groups[1].Value + match.Groups[2].Value,
					FontSize = 15,
					FontAttributes = FontAttributes.Bold,
					TextColor = BandReport,
					Margin = new Thickness(0, 0, 4, 0)
				});
				if (match.Groups[3].Value.Length > 0)
				{
					row.Children.Add(new Label
					{
						Text = match.Groups[3].Value,
						FontSize = 15,
						TextColor = Ink,
						LineBreakMode = LineBreakMode.WordWrap
					});
				}
				stack.Children.Add(row);
				continue;
			}
			stack.Children.Add(new Label
			{
				Text = line.Length == 0 ? " " : line,
				FontSize = 15,
				TextColor = Ink,
				LineBreakMode = LineBreakMode.WordWrap
			});
		}
		return stack;
	}

	static View MakeTitledBand(string title, Color band, Color bodyColor, View content)
	{
		var stack = new VerticalStackLayout { Spacing = 0, BackgroundColor = bodyColor };
		stack.Children.Add(new Label
		{
			Text = title,
			FontSize = 14,
			FontAttributes = FontAttributes.Bold,
			TextColor = Colors.White,
			BackgroundColor = band,
			Padding = new Thickness(10, 5),
			HorizontalOptions = LayoutOptions.Fill
		});
		stack.Children.Add(content);
		return new Border
		{
			BackgroundColor = bodyColor,
			Stroke = band,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = 0,
			Content = stack
		};
	}

	static View MakeBandCard(string title, Color band, Color bodyColor, params View[] fields)
	{
		var content = new VerticalStackLayout
		{
			Spacing = 6,
			Padding = new Thickness(10, 6, 10, 8),
			BackgroundColor = bodyColor
		};
		foreach (var field in fields) content.Children.Add(field);
		var stack = new VerticalStackLayout { Spacing = 0, BackgroundColor = bodyColor };
		stack.Children.Add(new Label
		{
			Text = title,
			FontSize = 14,
			FontAttributes = FontAttributes.Bold,
			TextColor = Colors.White,
			BackgroundColor = band,
			Padding = new Thickness(10, 5),
			HorizontalOptions = LayoutOptions.Fill
		});
		stack.Children.Add(content);
		return new Border
		{
			BackgroundColor = bodyColor,
			Stroke = band,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = 0,
			Content = stack
		};
	}

	static View MakePair(string leftLabel, string leftValue, string rightLabel, string rightValue)
	{
		var grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star)
			},
			ColumnSpacing = 8
		};
		grid.Add(MakePlainField(leftLabel, leftValue, 17));
		grid.Add(MakePlainField(rightLabel, FormatSlipNumber(rightValue), 17), 1, 0);
		return grid;
	}

	static string FormatSlipNumber(string? value)
	{
		var text = (value ?? "").Trim();
		if (text.Length <= 3 || !text.All(char.IsDigit)) return text;
		var parts = new List<string>();
		for (var index = 0; index < text.Length; index += 3)
			parts.Add(text.Substring(index, Math.Min(3, text.Length - index)));
		return string.Join(" ", parts);
	}

	static View MakePlainField(string label, string value, double valueSize = 15, bool emphasize = false, TextAlignment align = TextAlignment.Start)
	{
		var stack = new VerticalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Fill };
		stack.Children.Add(new Label
		{
			Text = label,
			FontSize = label is "受付No" or "伝票番号" ? 13 : 11,
			TextColor = Muted,
			HorizontalTextAlignment = align
		});
		stack.Children.Add(new Label
		{
			Text = DisplayValue(value),
			FontSize = label is "受付No" or "伝票番号" ? valueSize : valueSize,
			FontAttributes = emphasize ? FontAttributes.Bold : FontAttributes.None,
			TextColor = Ink,
			HorizontalTextAlignment = align,
			LineBreakMode = LineBreakMode.WordWrap
		});
		return stack;
	}

	static View MakeLongField(string label, string value)
	{
		var stack = new VerticalStackLayout { Spacing = 3 };
		stack.Children.Add(new Border
		{
			BackgroundColor = BandRequest,
			StrokeThickness = 0,
			Padding = new Thickness(8, 2),
			HorizontalOptions = LayoutOptions.Start,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
			Content = new Label
			{
				Text = label,
				FontSize = 11,
				FontAttributes = FontAttributes.Bold,
				TextColor = Colors.White
			}
		});
		stack.Children.Add(new Label
		{
			Text = DisplayValue(value),
			FontSize = 15,
			TextColor = Ink,
			LineHeight = 1.35,
			LineBreakMode = LineBreakMode.WordWrap
		});
		return stack;
	}

	View MakeAddressRow(string address)
	{
		var text = new VerticalStackLayout { Spacing = 1 };
		text.Children.Add(new Label { Text = "住所", FontSize = 11, TextColor = Muted });
		if (TrySplitAddress(address, out var postal, out var street, out var building))
		{
			text.Children.Add(AddressLine(postal, false));
			text.Children.Add(AddressLine(street, true));
			text.Children.Add(AddressLine(building, false));
		}
		else
		{
			text.Children.Add(AddressLine(DisplayValue(address), true));
		}
		return MakeTapRow("📍", text, CanMap(address), false, address);
	}

	static Label AddressLine(string text, bool emphasize)
	{
		return new Label
		{
			Text = text,
			FontSize = emphasize ? 19 : 18,
			FontAttributes = emphasize ? FontAttributes.Bold : FontAttributes.None,
			TextColor = Ink,
			LineBreakMode = LineBreakMode.WordWrap
		};
	}

	View MakeActionRow(string label, string value, bool priority = false)
	{
		var numberLine = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
		numberLine.Children.Add(new Label
		{
			Text = DisplayValue(value),
			FontSize = 19,
			FontAttributes = FontAttributes.Bold,
			TextColor = Ink,
			VerticalOptions = LayoutOptions.Center,
			LineBreakMode = LineBreakMode.WordWrap
		});
		if (priority) numberLine.Children.Add(PriorityBadge());
		var text = new VerticalStackLayout { Spacing = 0 };
		text.Children.Add(new Label { Text = label, FontSize = 11, TextColor = Muted });
		text.Children.Add(numberLine);
		return MakeTapRow("☎", text, CanDial(value), true, value);
	}

	static View PriorityBadge()
	{
		return new Border
		{
			BackgroundColor = Color.FromArgb("#FFE8C2"),
			StrokeThickness = 0,
			Padding = new Thickness(6, 1),
			VerticalOptions = LayoutOptions.Center,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
			Content = new Label
			{
				Text = "優先",
				FontSize = 12,
				FontAttributes = FontAttributes.Bold,
				TextColor = Color.FromArgb("#C2410C")
			}
		};
	}

	View MakeTapRow(string icon, View text, bool actionable, bool phone, string payload)
	{
		var grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 8,
			Padding = new Thickness(0, 3),
			MinimumHeightRequest = 40
		};
		grid.Add(new Label
		{
			Text = icon,
			FontSize = 18,
			TextColor = Ink,
			VerticalOptions = LayoutOptions.Center,
			WidthRequest = 22
		});
		grid.Add(text, 1, 0);
		var trail = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
		if (actionable)
		{
			trail.Children.Add(new Label
			{
				Text = "›",
				FontSize = 20,
				TextColor = Muted,
				VerticalOptions = LayoutOptions.Center
			});
			var tap = new TapGestureRecognizer();
			if (phone)
			{
				var number = payload;
				tap.Tapped += async (_, _) => await OpenPhoneAsync(number);
			}
			else
			{
				var address = payload;
				tap.Tapped += async (_, _) => await OpenAddressAsync(address);
			}
			grid.GestureRecognizers.Add(tap);
		}
		if (trail.Children.Count > 0) grid.Add(trail, 2, 0);
		return grid;
	}

	View MakeChargeSection(CustomerCard card)
	{
		var content = new VerticalStackLayout
		{
			Spacing = 4,
			Padding = new Thickness(8, 4, 8, 6),
			BackgroundColor = BodyCharge
		};
		if (card.Charges.Count == 0)
		{
			content.Children.Add(new Label { Text = "---", FontSize = 15, TextColor = Ink });
		}
		else
		{
			foreach (var row in card.Charges) content.Children.Add(MakeChargeCard(row));
		}
		content.Children.Add(MakeChargeTotal(card));
		var stack = new VerticalStackLayout { Spacing = 0, BackgroundColor = BodyCharge };
		stack.Children.Add(new Label
		{
			Text = "料金明細",
			FontSize = 14,
			FontAttributes = FontAttributes.Bold,
			TextColor = Colors.White,
			BackgroundColor = BandCharge,
			Padding = new Thickness(10, 5),
			HorizontalOptions = LayoutOptions.Fill
		});
		stack.Children.Add(content);
		return new Border
		{
			BackgroundColor = BodyCharge,
			Stroke = BandCharge,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = 0,
			Content = stack
		};
	}

	static View MakeChargeTotal(CustomerCard card)
	{
		var grid = new Grid
		{
			Padding = new Thickness(10, 6),
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			}
		};
		var caption = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
		caption.Children.Add(new Label
		{
			Text = "合計金額",
			FontSize = 13,
			FontAttributes = FontAttributes.Bold,
			TextColor = Color.FromArgb("#8A4B00")
		});
		if (HasDisplayValue(card.ChargeQuantityTotal))
		{
			caption.Children.Add(new Label
			{
				Text = "数量 " + DisplayValue(card.ChargeQuantityTotal),
				FontSize = 12,
				TextColor = Color.FromArgb("#8A4B00")
			});
		}
		grid.Add(caption);
		grid.Add(new Label
		{
			Text = DisplayMoney(card.ChargeTotal),
			FontSize = 20,
			FontAttributes = FontAttributes.Bold,
			TextColor = Color.FromArgb("#7A3E00"),
			HorizontalTextAlignment = TextAlignment.End
		}, 1, 0);
		return new Border
		{
			BackgroundColor = Color.FromArgb("#F6E2C4"),
			Stroke = BandCharge,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Padding = 0,
			Content = grid
		};
	}

	static View MakeChargeCard(ChargeCard row)
	{
		var body = new VerticalStackLayout { Spacing = 2 };
		body.Children.Add(new Label
		{
			Text = DisplayValue(row.ProductName),
			FontSize = 18,
			FontAttributes = FontAttributes.Bold,
			TextColor = Ink,
			LineBreakMode = LineBreakMode.WordWrap
		});
		var grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star)
			},
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto)
			},
			ColumnSpacing = 8,
			RowSpacing = 1
		};
		grid.Add(MakeChargeLine("商品コード", DisplayValue(row.ProductCode)), 0, 0);
		grid.Add(MakeChargeLine("金額", DisplayMoney(row.Amount)), 1, 0);
		grid.Add(MakeChargeLine("単価", DisplayMoney(row.UnitPrice)), 0, 1);
		grid.Add(MakeChargeLine("請求区分", DisplayValue(row.BillingType)), 1, 1);
		grid.Add(MakeChargeLine("数量", DisplayValue(row.Quantity)), 0, 2);
		body.Children.Add(grid);
		if (HasDisplayValue(row.Note))
		{
			body.Children.Add(new Label
			{
				Text = "備考 " + row.Note.Trim(),
				FontSize = 13,
				TextColor = Muted,
				LineBreakMode = LineBreakMode.WordWrap
			});
		}
		return new Border
		{
			BackgroundColor = Colors.White,
			Stroke = BandCharge,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Padding = new Thickness(8, 5),
			Content = body
		};
	}

	static Grid MakeChargeLine(string label, string value)
	{
		var grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(new GridLength(78)),
				new ColumnDefinition(GridLength.Star)
			},
			ColumnSpacing = 4
		};
		grid.Add(new Label { Text = label, FontSize = 13, TextColor = Muted, VerticalOptions = LayoutOptions.Center });
		grid.Add(ChargeValue(value), 1, 0);
		return grid;
	}

	static View ChargeValue(string value)
	{
		var word = CollectionWord(value);
		if (word != null)
		{
			return new Border
			{
				BackgroundColor = Color.FromArgb("#DC2626"),
				StrokeThickness = 0,
				Padding = new Thickness(8, 1),
				HorizontalOptions = LayoutOptions.End,
				VerticalOptions = LayoutOptions.Center,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
				Content = new Label
				{
					Text = word,
					FontSize = 13,
					FontAttributes = FontAttributes.Bold,
					TextColor = Colors.White
				}
			};
		}
		return new Label
		{
			Text = value,
			FontSize = 15,
			TextColor = Ink,
			HorizontalTextAlignment = TextAlignment.End,
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center
		};
	}

	static string? CollectionWord(string value)
	{
		var text = (value ?? "").Trim();
		if (text.Contains("直集", StringComparison.Ordinal)) return "直集";
		if (text.Contains("直収", StringComparison.Ordinal)) return "直収";
		return null;
	}

	static bool IsPriorityPhone(string phone, string? promise)
	{
		var digits = DigitsForDial(phone);
		if (digits.Length < 10) return false;
		foreach (var candidate in StarredPhones(promise))
		{
			if (candidate == digits) return true;
		}
		return false;
	}

	static IEnumerable<string> StarredPhones(string? promise)
	{
		var text = (promise ?? "").Replace('\u3000', ' ');
		var chars = text.ToCharArray();
		for (var i = 0; i < chars.Length; i++)
		{
			if (chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - '０' + '0');
			else if (chars[i] is '－' or 'ー' or '―' or '−') chars[i] = '-';
		}
		text = new string(chars);
		foreach (Match match in Regex.Matches(text, @"(?<![0-9])[0-9][0-9\-]{8,}[0-9](?![0-9])"))
		{
			if (!StarBeside(text, match.Index, match.Length)) continue;
			var digits = DigitsForDial(match.Value);
			if (digits.Length >= 10) yield return digits;
		}
	}

	static bool StarBeside(string text, int index, int length)
	{
		return StarAt(text, index, -1) || StarAt(text, index + length, 1);
	}

	static bool StarAt(string text, int start, int direction)
	{
		var index = direction < 0 ? start - 1 : start;
		var steps = 0;
		while (index >= 0 && index < text.Length && steps < 3 && (text[index] == ' ' || text[index] == '\t'))
		{
			index += direction;
			steps++;
		}
		if (index < 0 || index >= text.Length) return false;
		return text[index] is '☆' or '★';
	}

	static string DisplayNameWithHonorific(string? value)
	{
		var shown = DisplayValue(value);
		if (shown == "---" || shown.EndsWith("様", StringComparison.Ordinal)) return shown;
		return shown + " 様";
	}

	static bool TrySplitAddress(string address, out string postal, out string street, out string building)
	{
		postal = "";
		street = "";
		building = "";
		if (!HasDisplayValue(address)) return false;
		var normalized = address.Replace('\u3000', ' ').Trim();
		normalized = Regex.Replace(normalized, @"[ \t\f\v]+", " ");
		var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 3 || !IsPostalToken(parts[0])) return false;
		postal = parts[0];
		street = parts[1];
		building = string.Join(" ", parts.Skip(2));
		return building.Length > 0;
	}

	async Task OpenPhoneAsync(string number)
	{
		var dial = DigitsForDial(number);
		if (dial.Length == 0) return;
		try
		{
			if (PhoneDialer.Default.IsSupported)
			{
				PhoneDialer.Default.Open(dial);
				return;
			}
			await Launcher.Default.OpenAsync(new Uri("tel:" + dial));
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
		}
	}

	async Task OpenAddressAsync(string address)
	{
		if (!CanMap(address)) return;
		var encoded = Uri.EscapeDataString(MapQueryAddress(address));
		try
		{
			var geo = new Uri("geo:0,0?q=" + encoded);
			if (await Launcher.Default.CanOpenAsync(geo))
			{
				await Launcher.Default.OpenAsync(geo);
				return;
			}
			await Launcher.Default.OpenAsync(new Uri("https://www.google.com/maps/search/?api=1&query=" + encoded));
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
		}
	}

	static bool CanDial(string value)
	{
		return DigitsForDial(value).Length > 0;
	}

	static bool CanMap(string value)
	{
		return HasDisplayValue(value);
	}

	static string MapQueryAddress(string address)
	{
		var normalized = address.Replace('\u3000', ' ').Trim();
		normalized = Regex.Replace(normalized, @"[ \t\f\v]+", " ");
		var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length >= 3 && IsPostalToken(parts[0]))
			return parts[0] + " " + parts[1];
		return normalized;
	}

	static bool IsPostalToken(string token)
	{
		var text = token.StartsWith('〒') ? token[1..] : token;
		return Regex.IsMatch(text, @"^\d{3}-\d{4}$");
	}

	static string DigitsForDial(string value)
	{
		if (!HasDisplayValue(value)) return "";
		var digits = new StringBuilder();
		foreach (var ch in value)
		{
			if (char.IsDigit(ch)) digits.Append(ch);
			else if (ch is >= '０' and <= '９') digits.Append((char)(ch - '０' + '0'));
		}
		return digits.ToString();
	}

	static bool HasDisplayValue(string? value)
	{
		var text = (value ?? "").Trim();
		return text.Length > 0 && text != "---" && !text.Equals("null", StringComparison.OrdinalIgnoreCase);
	}

	static string DisplayValue(string? value)
	{
		return HasDisplayValue(value) ? value!.Trim() : "---";
	}

	static string DisplayMoney(string? value)
	{
		var shown = DisplayValue(value);
		if (shown == "---") return shown;
		var digits = shown.Replace(",", "", StringComparison.Ordinal).Trim();
		if (digits.Length > 0 && digits.All(char.IsDigit) && long.TryParse(digits, out var amount))
			return amount.ToString("N0") + "円";
		return shown;
	}

	static void AppendSurveyLine(StringBuilder body, string label, string value)
	{
		if (value.Length == 0) return;
		body.Append(label).Append(": ").AppendLine(value);
	}

	static string ReadSurveyString(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
		return value.GetString() ?? "";
	}

	static string RedactProbeText(string value)
	{
		var text = value.Trim();
		if (text.Length == 0) return "";
		var chars = text.ToCharArray();
		for (var i = 0; i < chars.Length; i++)
		{
			if (chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - '０' + '0');
			else if (chars[i] is '－' or 'ー' or '―' or '−') chars[i] = '-';
			else if (chars[i] == '＠') chars[i] = '@';
		}
		text = Regex.Replace(new string(chars), @"\s+", " ").Trim();
		text = Regex.Replace(text, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", "[EMAIL]", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"\d{2,4}-\d{2,4}-\d{3,4}", "[PHONE]");
		text = Regex.Replace(text, @"\d{7,}", "[NUMBER]");
		if (text.Length > 40) text = text[..40];
		return text;
	}

	async Task ShowResultAsync(string text)
	{
		if (MainThread.IsMainThread)
		{
			ResultLabel.Text = text;
			return;
		}
		await MainThread.InvokeOnMainThreadAsync(() => ResultLabel.Text = text);
	}

	async Task<string> DiagnoseNumberAsync(int step, string script, int expected)
	{
		var eval = await EvaluateAsync(script);
		if (eval.TimedOut) return "STEP" + step + "：タイムアウト";
		if (eval.Failed) return "STEP" + step + "：失敗\n例外：" + eval.ExceptionName;
		if (TryReadLength(eval.Raw, out var number) && number == expected) return "STEP" + step + "：成功 / " + expected;
		return "STEP" + step + "：失敗";
	}

	async Task<string> DiagnoseTitleAsync()
	{
		var eval = await EvaluateAsync("document.title");
		if (eval.TimedOut) return "STEP2：タイムアウト";
		if (eval.Failed) return "STEP2：失敗\n例外：" + eval.ExceptionName;
		var title = UnwrapJsString(eval.Raw);
		if (title == null) return "STEP2：失敗";
		return "STEP2：成功 / " + title;
	}

	async Task<string> DiagnoseUrlAsync()
	{
		var eval = await EvaluateAsync("location.href");
		if (eval.TimedOut) return "STEP3：タイムアウト";
		if (eval.Failed) return "STEP3：失敗\n例外：" + eval.ExceptionName;
		var href = UnwrapJsString(eval.Raw);
		if (string.IsNullOrWhiteSpace(href)) return "STEP3：失敗";
		return "STEP3：成功 / " + UrlPrivacy.ForDisplay(href);
	}

	async Task<string> DiagnoseCountAsync(int step, string script)
	{
		var eval = await EvaluateAsync(script);
		if (eval.TimedOut) return "STEP" + step + "：タイムアウト";
		if (eval.Failed) return "STEP" + step + "：失敗\n例外：" + eval.ExceptionName;
		if (!TryReadLength(eval.Raw, out var number) || number < 0) return "STEP" + step + "：失敗";
		return "STEP" + step + "：成功 / " + number;
	}

	void QueueAutoLogin()
	{
		if (!MainThread.IsMainThread)
		{
			MainThread.BeginInvokeOnMainThread(QueueAutoLogin);
			return;
		}
		if (loginPhase == LoginPhase.LoggedIn) return;
		if (loginInProgress) return;
		if (loginAttempts >= MaxLoginAttempts)
		{
			autoLoginArmed = false;
			loginPhase = LoginPhase.Failed;
			SetSessionStatus("Jシステム：ログイン失敗");
			ReportBridge(bridgeToken, "fail", null);
			ReportSearch(searchToken, "fail", null);
			ReportDetail(detailToken, "fail", null);
			return;
		}
		loginAttempts++;
		autoLoginArmed = true;
		loginInProgress = true;
		loginPhase = LoginPhase.LoggingIn;
		SetSessionStatus("Jシステム：ログイン中...");
		_ = AutoFillLoginAsync();
	}

	async Task ConfirmSessionAsync()
	{
		var eval = await EvaluateAsync("(function(){var nodes=document.querySelectorAll('input[type=\"password\"]');for(var i=0;i<nodes.length;i++){var el=nodes[i];if(el&&(el.offsetParent!==null||el.getClientRects().length>0))return 'form';}return 'in';})()");
		var code = UnwrapJsString(eval.Raw) ?? "";
		if (!eval.TimedOut && !eval.Failed && code == "form") return;
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			loginAttempts = 0;
			loginInProgress = false;
			autoLoginArmed = true;
			loginPhase = LoginPhase.LoggedIn;
			if (!IsLoginUrl(currentUrl)) settledUrl = currentUrl;
			if (FillStatusLabel.Text.Contains("ログイン送信", StringComparison.Ordinal))
				FillStatusLabel.Text = "自動ログイン：成功らしい";
			LoginStateLabel.Text = "ログイン状態：ログイン済みらしい";
			SessionStatusLabel.Text = "Jシステム：ログイン済み";
			TryResumeBridge();
			TryResumeSearch();
			TryResumeDetail();
		});
	}

	public void ReadFromRegulus(string receptionNo, int token, Action<int, string, CustomerCard?> callback)
	{
		if (receptionNo.Length != 8) return;
		bridgeToken = token;
		bridgeNo = receptionNo;
		bridgeCallback = callback;
		bridgeActive = true;
		bridgeStage = BridgeStage.Entry;
		bridgeResumeConsumed = false;
		bridgeArchiveUrl = "";
		ArmBridgeTimeout(token, 15);
		if (loginPhase == LoginPhase.LoggedIn || JudgeLogin(currentUrl) == "ログイン済みらしい")
		{
			bridgeResume = false;
			OpenUrl(CustomerUrlPrefix + receptionNo, "顧客ページ読み込み中");
			return;
		}
		bridgeResume = true;
		if (loginPhase == LoginPhase.LoggingIn || IsLoginUrl(currentUrl)) return;
		bridgeActive = false;
		bridgeResume = false;
		callback(token, "fail", null);
	}

	public View CreateSharedCustomerView(CustomerCard card)
	{
		var stack = new VerticalStackLayout { Spacing = 8, BackgroundColor = PageBg };
		AddCustomerSections(stack, card);
		return stack;
	}

	public void BeginReceptionSearch(int token, string receptionNo, string customerName, string tel, string pointCardNo, string slipNo, Action<int, string, IReadOnlyList<SearchHit>?> callback)
	{
		searchToken = token;
		searchActive = true;
		searchCallback = callback;
		searchReception = (receptionNo ?? "").Trim();
		searchName = (customerName ?? "").Trim();
		searchTel = (tel ?? "").Trim();
		searchPoint = (pointCardNo ?? "").Trim();
		searchSlip = (slipNo ?? "").Trim();
		detailActive = false;
		detailNeedsResume = false;
		detailResumedOnce = false;
		detailTimeoutToken = 0;
		searchNeedsResume = false;
		searchResumedOnce = false;
		ArmSearchTimeout(token);
		EnsureWebViewHandler();
		if (loginPhase != LoginPhase.LoggedIn && JudgeLogin(currentUrl) != "ログイン済みらしい")
		{
			searchNeedsResume = true;
			if (loginPhase == LoginPhase.LoggingIn || IsLoginUrl(currentUrl)) return;
			ReportSearch(token, "fail", null);
			return;
		}
		if (IsSearchReceptionPage(currentUrl))
		{
			viewState = "検索ページ読み込み中";
			_ = SubmitReceptionSearchAsync();
			return;
		}
		OpenUrl(SearchUrl, "検索ページ読み込み中");
	}

	public void OpenHistoryDetail(int token, string detailUrl, Action<int, string, CustomerCard?> callback)
	{
		detailToken = token;
		detailCallback = callback;
		detailActive = true;
		detailNeedsResume = false;
		detailResumedOnce = false;
		var normalized = NormalizeDetailPath(detailUrl);
		if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
			|| !uri.AbsolutePath.Contains("history_request_sub.php", StringComparison.OrdinalIgnoreCase))
		{
			ReportDetail(token, "fail", null);
			return;
		}
		detailTarget = normalized;
		ArmDetailTimeout(token);
		EnsureWebViewHandler();
		if (loginPhase != LoginPhase.LoggedIn && JudgeLogin(currentUrl) != "ログイン済みらしい")
		{
			detailNeedsResume = true;
			if (loginPhase == LoginPhase.LoggingIn || IsLoginUrl(currentUrl)) return;
			ReportDetail(token, "fail", null);
			return;
		}
		OpenUrl(detailTarget, "検索詳細読み込み中");
	}

	async Task SubmitReceptionSearchAsync()
	{
		var token = searchToken;
		if (!searchActive || token != searchToken) return;
		viewState = "検索結果読み込み中";
		var script = BuildSearchSubmitScript(searchReception, searchName, searchTel, searchPoint, searchSlip);
		var eval = await EvaluateAsync(script);
		if (!searchActive || token != searchToken) return;
		var code = UnwrapJsString(eval.Raw) ?? "";
		if (!eval.TimedOut && !eval.Failed && code == "no-form")
			ReportSearch(token, "fail", null);
	}

	async Task FinishReceptionSearchAsync()
	{
		var token = searchToken;
		await WaitUntilReadyAsync(() => searchActive && token == searchToken);
		if (!searchActive || token != searchToken) return;
		var eval = await EvaluateAsync(BuildSearchReadScript());
		if (!searchActive || token != searchToken) return;
		if (eval.TimedOut || eval.Failed || !TryBindSearchHits(eval.Raw, out var hits))
		{
			ReportSearch(token, "fail", null);
			return;
		}
		ReportSearch(token, "ok", hits);
	}

	async Task FinishHistoryDetailAsync()
	{
		var token = detailToken;
		await WaitUntilReadyAsync(() => detailActive && token == detailToken);
		if (!detailActive || token != detailToken) return;
		var eval = await EvaluateAsync(BuildCustomerReadScript());
		if (!detailActive || token != detailToken) return;
		if (eval.TimedOut || eval.Failed || !TryBindCustomer(eval.Raw, out var card) || card == null)
		{
			ReportDetail(token, "fail", null);
			return;
		}
		ReportDetail(token, "ok", card);
	}

	async Task WaitUntilReadyAsync(Func<bool> stillCurrent)
	{
		for (var i = 0; i < 6; i++)
		{
			if (!stillCurrent()) return;
			var eval = await EvaluateAsync("document.readyState");
			var state = UnwrapJsString(eval.Raw) ?? "";
			if (!eval.TimedOut && !eval.Failed && state == "complete")
			{
				await Task.Delay(200);
				return;
			}
			await Task.Delay(200);
		}
	}

	void ReportSearch(int token, string status, IReadOnlyList<SearchHit>? hits)
	{
		if (!searchActive || token != searchToken) return;
		searchActive = false;
		searchNeedsResume = false;
		searchTimeoutToken = 0;
		searchReception = "";
		searchName = "";
		searchTel = "";
		searchPoint = "";
		searchSlip = "";
		var callback = searchCallback;
		searchCallback = null;
		callback?.Invoke(token, status, hits);
	}

	void ReportDetail(int token, string status, CustomerCard? card)
	{
		if (!detailActive || token != detailToken) return;
		detailActive = false;
		detailNeedsResume = false;
		detailTimeoutToken = 0;
		detailTarget = "";
		var callback = detailCallback;
		detailCallback = null;
		callback?.Invoke(token, status, card);
	}

	void ArmSearchTimeout(int token)
	{
		searchTimeoutToken = token;
		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(15));
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				if (searchTimeoutToken != token) return;
				if (loginPhase == LoginPhase.LoggingIn && searchNeedsResume)
				{
					ArmSearchTimeout(token);
					return;
				}
				ReportSearch(token, "fail", null);
			});
		});
	}

	void ArmDetailTimeout(int token)
	{
		detailTimeoutToken = token;
		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(10));
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				if (detailTimeoutToken != token) return;
				if (loginPhase == LoginPhase.LoggingIn && detailNeedsResume)
				{
					ArmDetailTimeout(token);
					return;
				}
				ReportDetail(token, "fail", null);
			});
		});
	}

	static bool IsSearchReceptionPage(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		return uri.AbsolutePath.Contains("/search/search_reception.php", StringComparison.OrdinalIgnoreCase);
	}

	static bool IsSameHistoryDetail(string current, string expected)
	{
		if (!Uri.TryCreate(current, UriKind.Absolute, out var left)) return false;
		if (!Uri.TryCreate(expected, UriKind.Absolute, out var right)) return false;
		if (!left.AbsolutePath.Contains("history_request_sub.php", StringComparison.OrdinalIgnoreCase)) return false;
		if (!string.Equals(left.AbsolutePath, right.AbsolutePath, StringComparison.OrdinalIgnoreCase)) return false;
		var leftQuery = QueryMap(left.Query);
		var rightQuery = QueryMap(right.Query);
		return leftQuery.TryGetValue("id", out var id) && id.Length > 0
			&& rightQuery.TryGetValue("id", out var expectedId) && id == expectedId
			&& leftQuery.TryGetValue("no", out var no)
			&& rightQuery.TryGetValue("no", out var expectedNo) && no == expectedNo;
	}

	static Dictionary<string, string> QueryMap(string query)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		var text = query.TrimStart('?');
		if (text.Length == 0) return map;
		foreach (var part in text.Split('&'))
		{
			var pair = part.Split('=', 2);
			if (pair.Length == 0 || pair[0].Length == 0) continue;
			map[Uri.UnescapeDataString(pair[0])] = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
		}
		return map;
	}

	static string BuildSearchSubmitScript(string reception, string name, string tel, string point, string slip)
	{
		var payload = JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["srch_reception_no"] = reception,
			["srch_user_name"] = name,
			["srch_tel"] = tel,
			["srch_point_card_no"] = point,
			["srch_slip_no"] = slip
		});
		var literal = JsonSerializer.Serialize(payload);
		return "(function(){var data=JSON.parse(" + literal + @");
var form=document.querySelector('form[name=""frm""]');
if(!form) return 'no-form';
function set(name,value){var el=form.querySelector('input[name=""'+name+'""]');if(el) el.value=value;}
set('mode','search');
set('srch_reception_no', data.srch_reception_no||'');
set('srch_user_name', data.srch_user_name||'');
set('srch_tel', data.srch_tel||'');
set('srch_point_card_no', data.srch_point_card_no||'');
set('srch_slip_no', data.srch_slip_no||'');
form.action='search_reception.php';
form.submit();
return 'ok';
})()";
	}

	static string BuildSearchReadScript()
	{
		return "(function(){" +
			"function clean(el){return String(el&&el.textContent||'').replace(/\\u00a0/g,' ').replace(/\\s+/g,' ').replace(/^ +/,'').replace(/ +$/,'');}" +
			"function col(tr,cls){var nodes=tr.querySelectorAll('td.'+cls);var out=[];for(var i=0;i<nodes.length;i++) out.push(clean(nodes[i]));return out;}" +
			"function detailOf(tr){var btn=tr.querySelector('td.edit2 input[value=\"依頼の照会\"]');if(!btn) return '';var raw=btn.getAttribute('onclick')||'';var m=/openPopup\\(\\s*'([^']*)'/.exec(raw);return m&&m[1]&&m[1].indexOf('history_request_sub.php')>=0?m[1]:'';}" +
			"var rows=document.querySelectorAll('#searchList tr.line');var list=[];" +
			"for(var r=0;r<rows.length;r++){var tr=rows[r];var no=col(tr,'no2');var names=col(tr,'name2');var dates=col(tr,'date');" +
			"list.push({receptionNo:no[0]||'',slipNo:no[1]||'',customerName:names[0]||'',furigana:names[1]||'',planName:clean(tr.querySelector('td.plan2')),requestDate:dates[0]||'',confirmedDate:dates[1]||'',completedDate:dates[2]||'',status:clean(tr.querySelector('td.status2')),trader:clean(tr.querySelector('td.trader2')),detailPath:detailOf(tr)});}" +
			"return encodeURIComponent(JSON.stringify(list));})()";
	}

	static bool TryBindSearchHits(string? raw, out List<SearchHit> hits)
	{
		hits = new List<SearchHit>();
		if (!TryReadEncodedJson(raw, JsonValueKind.Array, out var document, out _) || document == null) return false;
		using (document)
		{
			foreach (var item in document.RootElement.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object) continue;
				hits.Add(new SearchHit
				{
					ReceptionNo = ReadSurveyString(item, "receptionNo"),
					SlipNo = ReadSurveyString(item, "slipNo"),
					CustomerName = ReadSurveyString(item, "customerName"),
					Furigana = ReadSurveyString(item, "furigana"),
					PlanName = ReadSurveyString(item, "planName"),
					RequestDate = ReadSurveyString(item, "requestDate"),
					ConfirmedDate = ReadSurveyString(item, "confirmedDate"),
					CompletedDate = ReadSurveyString(item, "completedDate"),
					Status = ReadSurveyString(item, "status"),
					Trader = ReadSurveyString(item, "trader"),
					DetailPath = NormalizeDetailPath(ReadSurveyString(item, "detailPath"))
				});
			}
			return true;
		}
	}

	void TryResumeBridge()
	{
		if (!bridgeActive || !bridgeResume || bridgeNo.Length != 8) return;
		bridgeResume = false;
		bridgeResumeConsumed = true;
		NoteSessionRestored();
		if (bridgeStage == BridgeStage.ArchiveDetail && bridgeArchiveUrl.Length > 0)
		{
			OpenUrl(bridgeArchiveUrl, "過去案件読み込み中");
			return;
		}
		if (bridgeStage == BridgeStage.ArchiveSearch)
		{
			if (IsSearchReceptionPage(currentUrl))
			{
				viewState = "過去検索読み込み中";
				_ = SubmitBridgeSearchAsync();
				return;
			}
			OpenUrl(SearchUrl, "過去検索読み込み中");
			return;
		}
		OpenUrl(CustomerUrlPrefix + bridgeNo, "顧客ページ読み込み中");
	}

	void TryResumeSearch()
	{
		if (!searchActive || !searchNeedsResume || searchResumedOnce) return;
		searchNeedsResume = false;
		searchResumedOnce = true;
		NoteSessionRestored();
		if (IsSearchReceptionPage(currentUrl))
		{
			viewState = "検索ページ読み込み中";
			_ = SubmitReceptionSearchAsync();
			return;
		}
		OpenUrl(SearchUrl, "検索ページ読み込み中");
	}

	void TryResumeDetail()
	{
		if (!detailActive || !detailNeedsResume || detailResumedOnce || detailTarget.Length == 0) return;
		detailNeedsResume = false;
		detailResumedOnce = true;
		NoteSessionRestored();
		OpenUrl(detailTarget, "検索詳細読み込み中");
	}

	void NoteSessionRestored()
	{
		if (loginPhase == LoginPhase.LoggedIn) return;
		loginAttempts = 0;
		loginInProgress = false;
		autoLoginArmed = true;
		loginPhase = LoginPhase.LoggedIn;
		if (!IsLoginUrl(currentUrl)) settledUrl = currentUrl;
		SessionStatusLabel.Text = "Jシステム：ログイン済み";
	}

	static string NormalizeDetailPath(string raw)
	{
		var text = (raw ?? "").Trim();
		var marker = "history_request_sub.php";
		var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (index < 0) return "";
		var query = "";
		var queryIndex = text.IndexOf('?', index);
		if (queryIndex >= 0) query = text[queryIndex..];
		if (!query.Contains("id=", StringComparison.Ordinal)) return "";
		return "https://service.joshin.co.jp/pc_support/trader/history/history_request_sub.php" + query;
	}

	void ReportBridge(int token, string status, CustomerCard? card)
	{
		if (!bridgeActive || token != bridgeToken) return;
		bridgeActive = false;
		bridgeResume = false;
		bridgeTimeoutToken = 0;
		bridgeCallback?.Invoke(token, status, card);
	}

	void ArmBridgeTimeout(int token, int seconds)
	{
		var generation = ++bridgeTimeoutGeneration;
		bridgeTimeoutToken = token;
		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(seconds));
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				if (generation != bridgeTimeoutGeneration || bridgeTimeoutToken != token) return;
				if (loginPhase == LoginPhase.LoggingIn && bridgeResume)
				{
					ArmBridgeTimeout(token, seconds);
					return;
				}
				ReportBridge(token, "fail", null);
			});
		});
	}

	async Task FinishBridgeReadAsync(int token, string receptionNo)
	{
		await WaitForCustomerDocumentAsync(token);
		if (token != bridgeToken || !bridgeActive) return;
		if (!currentUrl.Contains("no=" + receptionNo, StringComparison.Ordinal) || !IsCustomerDetailPage(currentUrl))
		{
			BeginArchiveSearch(token);
			return;
		}
		var eval = await EvaluateAsync(BuildCustomerReadScript());
		if (token != bridgeToken || !bridgeActive) return;
		if (!eval.TimedOut && !eval.Failed && TryBindCustomer(eval.Raw, out var card) && card != null && IsCurrentCase(card, receptionNo))
		{
			await MainThread.InvokeOnMainThreadAsync(() => ReportBridge(token, "ok", card));
			return;
		}
		BeginArchiveSearch(token);
	}

	static bool IsCurrentCase(CustomerCard card, string receptionNo)
	{
		if (!SameReception(card.ReceptionNo, receptionNo)) return false;
		return HasDisplayValue(card.CustomerName)
			|| HasDisplayValue(card.Phone1)
			|| HasDisplayValue(card.Address)
			|| HasDisplayValue(card.SlipNo)
			|| card.Charges.Count > 0;
	}

	static bool SameReception(string value, string receptionNo)
	{
		return DigitsForDial(value) == receptionNo;
	}

	void BeginArchiveSearch(int token)
	{
		if (!MainThread.IsMainThread)
		{
			MainThread.BeginInvokeOnMainThread(() => BeginArchiveSearch(token));
			return;
		}
		if (!bridgeActive || token != bridgeToken || bridgeStage != BridgeStage.Entry) return;
		bridgeStage = BridgeStage.ArchiveSearch;
		bridgeResume = false;
		bridgeResumeConsumed = false;
		ArmBridgeTimeout(token, 25);
		if (loginPhase != LoginPhase.LoggedIn && JudgeLogin(currentUrl) != "ログイン済みらしい")
		{
			bridgeResume = true;
			if (loginPhase == LoginPhase.LoggingIn || IsLoginUrl(currentUrl)) return;
			ReportBridge(token, "fail", null);
			return;
		}
		if (IsSearchReceptionPage(currentUrl))
		{
			viewState = "過去検索読み込み中";
			_ = SubmitBridgeSearchAsync();
			return;
		}
		OpenUrl(SearchUrl, "過去検索読み込み中");
	}

	async Task SubmitBridgeSearchAsync()
	{
		var token = bridgeToken;
		if (!bridgeActive || token != bridgeToken || bridgeStage != BridgeStage.ArchiveSearch) return;
		viewState = "過去検索結果読み込み中";
		var eval = await EvaluateAsync(BuildSearchSubmitScript(bridgeNo, "", "", "", ""));
		if (!bridgeActive || token != bridgeToken) return;
		var code = UnwrapJsString(eval.Raw) ?? "";
		if (!eval.TimedOut && !eval.Failed && code == "no-form")
			ReportBridge(token, "fail", null);
	}

	async Task FinishBridgeSearchAsync()
	{
		var token = bridgeToken;
		await WaitUntilReadyAsync(() => bridgeActive && token == bridgeToken && bridgeStage == BridgeStage.ArchiveSearch);
		if (!bridgeActive || token != bridgeToken) return;
		var eval = await EvaluateAsync(BuildSearchReadScript());
		if (!bridgeActive || token != bridgeToken) return;
		if (eval.TimedOut || eval.Failed || !TryBindSearchHits(eval.Raw, out var hits))
		{
			await MainThread.InvokeOnMainThreadAsync(() => ReportBridge(token, "fail", null));
			return;
		}
		var path = PickArchivePath(hits, bridgeNo);
		if (path.Length == 0)
		{
			await MainThread.InvokeOnMainThreadAsync(() => ReportBridge(token, "missing", null));
			return;
		}
		OpenArchiveDetail(token, path);
	}

	static string PickArchivePath(IReadOnlyList<SearchHit> hits, string receptionNo)
	{
		foreach (var hit in hits)
		{
			if (!SameReception(hit.ReceptionNo, receptionNo)) continue;
			if (!hit.DetailPath.Contains("history_request_sub.php", StringComparison.OrdinalIgnoreCase)) continue;
			return hit.DetailPath;
		}
		return "";
	}

	void OpenArchiveDetail(int token, string path)
	{
		if (!MainThread.IsMainThread)
		{
			MainThread.BeginInvokeOnMainThread(() => OpenArchiveDetail(token, path));
			return;
		}
		if (!bridgeActive || token != bridgeToken) return;
		var normalized = NormalizeDetailPath(path);
		if (normalized.Length == 0)
		{
			ReportBridge(token, "missing", null);
			return;
		}
		bridgeStage = BridgeStage.ArchiveDetail;
		bridgeArchiveUrl = normalized;
		bridgeResume = false;
		bridgeResumeConsumed = false;
		ArmBridgeTimeout(token, 15);
		if (loginPhase != LoginPhase.LoggedIn && JudgeLogin(currentUrl) != "ログイン済みらしい")
		{
			bridgeResume = true;
			if (loginPhase == LoginPhase.LoggingIn || IsLoginUrl(currentUrl)) return;
			ReportBridge(token, "fail", null);
			return;
		}
		OpenUrl(normalized, "過去案件読み込み中");
	}

	async Task FinishBridgeHistoryAsync()
	{
		var token = bridgeToken;
		await WaitUntilReadyAsync(() => bridgeActive && token == bridgeToken && bridgeStage == BridgeStage.ArchiveDetail);
		if (!bridgeActive || token != bridgeToken) return;
		var eval = await EvaluateAsync(BuildCustomerReadScript());
		if (!bridgeActive || token != bridgeToken) return;
		if (eval.TimedOut || eval.Failed || !TryBindCustomer(eval.Raw, out var card) || card == null || !HasAnyDetail(card))
		{
			await MainThread.InvokeOnMainThreadAsync(() => ReportBridge(token, "fail", null));
			return;
		}
		await MainThread.InvokeOnMainThreadAsync(() => ReportBridge(token, "ok", card));
	}

	static bool HasAnyDetail(CustomerCard card)
	{
		return HasDisplayValue(card.CustomerName)
			|| HasDisplayValue(card.ReceptionNo)
			|| HasDisplayValue(card.SlipNo)
			|| HasDisplayValue(card.CompletionReport)
			|| card.Charges.Count > 0
			|| card.Responses.Count > 0;
	}

	async Task WaitForCustomerDocumentAsync(int token)
	{
		for (var i = 0; i < 6; i++)
		{
			if (token != bridgeToken || !bridgeActive) return;
			var eval = await EvaluateAsync("document.readyState");
			var state = UnwrapJsString(eval.Raw) ?? "";
			if (!eval.TimedOut && !eval.Failed && state == "complete")
			{
				await Task.Delay(200);
				return;
			}
			await Task.Delay(200);
		}
	}

	void SetSessionStatus(string text)
	{
		if (MainThread.IsMainThread)
		{
			SessionStatusLabel.Text = text;
			return;
		}
		MainThread.BeginInvokeOnMainThread(() => SessionStatusLabel.Text = text);
	}

	async Task AutoFillLoginAsync()
	{
		var missingCredentials = false;
		try
		{
			var stored = await JSystemCredentialStore.LoadAsync();
			if (string.IsNullOrEmpty(stored.UserId) || string.IsNullOrEmpty(stored.Password))
			{
				missingCredentials = true;
				loginPhase = LoginPhase.CredentialsMissing;
				FillStatusLabel.Text = "保存済み認証情報：なし";
				SetSessionStatus("Jシステム：ログイン情報未設定");
				return;
			}
			if (JudgeLogin(currentUrl) != "ログイン画面") return;
			await Task.Delay(50);
			if (JudgeLogin(currentUrl) != "ログイン画面") return;
			ConfigureAndroidWebView();
			var eval = await EvaluateAsync(BuildFillScript(stored.UserId, stored.Password));
			if (JudgeLogin(currentUrl) != "ログイン画面") return;
			if (eval.TimedOut)
			{
				FillStatusLabel.Text = "自動ログイン：失敗\n理由：タイムアウト";
				loginPhase = LoginPhase.Failed;
				SetSessionStatus("Jシステム：ログイン失敗");
				return;
			}
			var code = UnwrapJsString(eval.Raw) ?? "";
			if (eval.Failed || code == "ambiguous" || code == "no-id" || code == "no-form" || code == "no-password")
			{
				FillStatusLabel.Text = "自動ログイン：失敗\n理由：ログイン入力欄を特定できません";
				loginPhase = LoginPhase.Failed;
				SetSessionStatus("Jシステム：ログイン失敗");
				return;
			}
			if (code == "ok")
			{
				FillStatusLabel.Text = "自動ログイン：ID/PW入力成功";
				await SubmitLoginAsync();
				return;
			}
			FillStatusLabel.Text = "自動ログイン：失敗\n理由：ログイン入力欄を特定できません";
			loginPhase = LoginPhase.Failed;
			SetSessionStatus("Jシステム：ログイン失敗");
		}
		catch (Exception ex)
		{
			FillStatusLabel.Text = "自動ログイン：失敗\n例外：" + ex.GetType().Name;
			loginPhase = LoginPhase.Failed;
			SetSessionStatus("Jシステム：ログイン失敗");
		}
		finally
		{
			loginInProgress = false;
			if (missingCredentials) loginAttempts = MaxLoginAttempts;
			else _ = RetryIfStillOnLoginAsync();
		}
	}

	async Task RetryIfStillOnLoginAsync()
	{
		await Task.Delay(800);
		if (loginPhase == LoginPhase.LoggedIn) return;
		if (loginInProgress || JudgeLogin(currentUrl) != "ログイン画面") return;
		if (loginAttempts >= MaxLoginAttempts)
		{
			autoLoginArmed = false;
			loginPhase = LoginPhase.Failed;
			SetSessionStatus("Jシステム：ログイン失敗");
			ReportBridge(bridgeToken, "fail", null);
			ReportSearch(searchToken, "fail", null);
			ReportDetail(detailToken, "fail", null);
			return;
		}
		QueueAutoLogin();
	}

	static string BuildFillScript(string userId, string password)
	{
		var payload = JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["userId"] = userId,
			["password"] = password
		});
		var literal = JsonSerializer.Serialize(payload);
		return "(function(){var creds=JSON.parse(" + literal + @");
function shown(el){return !!(el && (el.offsetParent!==null || el.getClientRects().length>0));}
var passwords=[];
var all=document.querySelectorAll('input[type=""password""]');
for (var i=0;i<all.length;i++){if(shown(all[i])) passwords.push(all[i]);}
if(passwords.length===0) return 'no-password';
var password=passwords[0];
var form=password.form;
if(!form) return 'no-form';
var ids=[];
var inputs=form.querySelectorAll('input');
for (var j=0;j<inputs.length;j++){
var el=inputs[j];
if(el===password) continue;
var type=(el.getAttribute('type')||'text').toLowerCase();
if(type!=='text' && type!=='email') continue;
if(!shown(el)) continue;
ids.push(el);
}
if(ids.length===0) return 'no-id';
if(ids.length!==1) return 'ambiguous';
function setValue(el,value){el.focus();el.value=value;el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));}
setValue(ids[0], creds.userId);
setValue(password, creds.password);
return 'ok';
})()";
	}

	async Task SubmitLoginAsync()
	{
		if (!autoLoginArmed || JudgeLogin(currentUrl) != "ログイン画面") return;
		autoLoginArmed = false;
		FillStatusLabel.Text = "自動ログイン：ログイン送信";
		var eval = await EvaluateAsync(BuildSubmitScript());
		if (JudgeLogin(currentUrl) != "ログイン画面" && !eval.TimedOut && !eval.Failed) return;
		var code = UnwrapJsString(eval.Raw) ?? "";
		if (eval.TimedOut)
		{
			FillStatusLabel.Text = "自動ログイン：失敗\n理由：タイムアウト";
			return;
		}
		if (eval.Failed || code != "ok")
		{
			FillStatusLabel.Text = "自動ログイン：失敗\n理由：ログインボタンを特定できません";
			return;
		}
		FillStatusLabel.Text = "自動ログイン：ログイン送信";
	}

	static string BuildSubmitScript()
	{
		return @"
(function(){
function shown(el){return !!(el && (el.offsetParent!==null || el.getClientRects().length>0));}
var passwords=[];
var all=document.querySelectorAll('input[type=""password""]');
for (var i=0;i<all.length;i++){if(shown(all[i])) passwords.push(all[i]);}
if(passwords.length!==1) return 'no-button';
var form=passwords[0].form;
if(!form) return 'no-button';
var found=[];
var nodes=form.querySelectorAll('button, input');
for (var j=0;j<nodes.length;j++){
var el=nodes[j];
var tag=el.tagName.toLowerCase();
var type=(el.getAttribute('type')||'').toLowerCase();
if(tag==='input' && type==='submit' && shown(el)) found.push(el);
if(tag==='button' && type==='submit' && shown(el)) found.push(el);
}
if(found.length!==1) return 'no-button';
found[0].click();
return 'ok';
})()";
	}

	Task<JsEval> EvaluateAsync(string script)
	{
		var done = new TaskCompletionSource<JsEval>(TaskCreationOptions.RunContinuationsAsynchronously);
		var timer = new Timer(_ => done.TrySetResult(JsEval.Timeout()), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
		MainThread.BeginInvokeOnMainThread(() =>
		{
			try
			{
				ConfigureAndroidWebView();
				var evalTask = JWeb.EvaluateJavaScriptAsync(script);
				evalTask.ContinueWith(task =>
				{
					timer.Dispose();
					if (task.IsFaulted)
					{
						var error = task.Exception?.GetBaseException() ?? new InvalidOperationException();
						done.TrySetResult(JsEval.Fail(error));
						return;
					}
					done.TrySetResult(JsEval.Ok(task.Result));
				}, TaskScheduler.Default);
			}
			catch (Exception ex)
			{
				timer.Dispose();
				done.TrySetResult(JsEval.Fail(ex));
			}
		});
		return done.Task;
	}

	static string JudgeLogin(string url)
	{
		if (string.IsNullOrWhiteSpace(url)) return "未確認";
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "判定不能";
		var path = uri.AbsolutePath.ToLowerInvariant();
		if (path.Contains("login", StringComparison.Ordinal)) return "ログイン画面";
		if (uri.Host.Equals("service.joshin.co.jp", StringComparison.OrdinalIgnoreCase)
			&& path.Contains("/pc_support/trader/", StringComparison.Ordinal)
			&& !path.Contains("login", StringComparison.Ordinal))
			return "ログイン済みらしい";
		return "判定不能";
	}

	static string DescribeNavigation(WebNavigationResult result)
	{
		if (result == WebNavigationResult.Timeout) return "タイムアウト";
		if (result == WebNavigationResult.Cancel) return "読み込みが中断されました";
		if (result == WebNavigationResult.Failure) return "ページを読み込めませんでした";
		return result.ToString();
	}

	static string? UnwrapJsString(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return null;
		var text = raw.Trim();
		if (text.Length >= 2 && text[0] == '"')
		{
			try
			{
				return JsonSerializer.Deserialize<string>(text);
			}
			catch
			{
				return null;
			}
		}
		return text;
	}

	static bool TryReadLength(string? raw, out int length)
	{
		length = 0;
		var text = UnwrapJsString(raw);
		return text != null && int.TryParse(text, out length) && length >= 0;
	}

	static bool IsReceptionNo(string? value)
	{
		var text = (value ?? "").Trim();
		return text.Length == 8 && text.All(char.IsDigit);
	}

	static string SafeError(Exception ex)
	{
		var message = ex.Message ?? "";
		if (message.Length == 0 || message.Length > 160 || message.Contains('<', StringComparison.Ordinal)) return ex.GetType().Name;
		if (message.Contains("password", StringComparison.OrdinalIgnoreCase)) return ex.GetType().Name;
		return ex.GetType().Name + " " + message;
	}

	readonly record struct JsEval(bool TimedOut, bool Failed, string? Raw, string ExceptionName)
	{
		public static JsEval Timeout() => new(true, true, null, "");
		public static JsEval Ok(string? raw) => new(false, false, raw, "");
		public static JsEval Fail(Exception ex) => new(false, true, null, ex.GetType().Name);
	}

	public sealed class CustomerCard
	{
		public string ReceptionNo { get; init; } = "";
		public string SlipNo { get; init; } = "";
		public string StoreCode { get; init; } = "";
		public string StoreName { get; init; } = "";
		public string ReceptionistCode { get; init; } = "";
		public string ReceptionistName { get; init; } = "";
		public string CustomerName { get; init; } = "";
		public string Furigana { get; init; } = "";
		public string Address { get; init; } = "";
		public string Phone1 { get; init; } = "";
		public string Phone2 { get; init; } = "";
		public string VisitConfirmed { get; init; } = "";
		public string Worker { get; init; } = "";
		public string VisitRequested { get; init; } = "";
		public string RequestUseField { get; init; } = "";
		public string RequestDate { get; init; } = "";
		public string VisitScheduled { get; init; } = "";
		public string RequestDetails { get; init; } = "";
		public string PromiseDetails { get; init; } = "";
		public string RequestSupplement { get; init; } = "";
		public string PromiseSupplement { get; init; } = "";
		public string ContractNo { get; init; } = "";
		public string ChargeTotal { get; init; } = "";
		public string ChargeQuantityTotal { get; init; } = "";
		public string CompletionReport { get; init; } = "";
		public string CompletionDate { get; init; } = "";
		public string CompletionWorker { get; init; } = "";
		public string CenterConfirmed { get; init; } = "";
		public string ConfirmResult { get; init; } = "";
		public bool HasDirectCollection { get; init; }
		public List<ChargeCard> Charges { get; } = new();
		public List<ResponseCard> Responses { get; } = new();
		public List<ProgressCard> Progress { get; } = new();
	}

	public sealed class ChargeCard
	{
		public string ProductCode { get; init; } = "";
		public string ProductName { get; init; } = "";
		public string UnitPrice { get; init; } = "";
		public string Quantity { get; init; } = "";
		public string Amount { get; init; } = "";
		public string BillingType { get; init; } = "";
		public string Note { get; init; } = "";
	}

	public sealed class ResponseCard
	{
		public string At { get; init; } = "";
		public string Party { get; init; } = "";
		public string Kind { get; init; } = "";
		public string Left { get; init; } = "";
		public string Right { get; init; } = "";
	}

	public sealed class ProgressCard
	{
		public string Text { get; init; } = "";
	}

	public sealed class SearchHit
	{
		public string ReceptionNo { get; init; } = "";
		public string SlipNo { get; init; } = "";
		public string CustomerName { get; init; } = "";
		public string Furigana { get; init; } = "";
		public string PlanName { get; init; } = "";
		public string RequestDate { get; init; } = "";
		public string ConfirmedDate { get; init; } = "";
		public string CompletedDate { get; init; } = "";
		public string Status { get; init; } = "";
		public string Trader { get; init; } = "";
		public string DetailPath { get; init; } = "";
	}
}
