namespace RegulusMobile;

public partial class JSystemPage
{
	static readonly TimeSpan ReLoginAfter = TimeSpan.FromMinutes(10);
	TimeProvider sessionClock = TimeProvider.System;
	DateTimeOffset? deactivatedAt;
	bool appInactive;
	int sessionGeneration;
	int navigationGeneration;
	int loginTimeoutGeneration;
	int searchTimeoutGeneration;
	int detailTimeoutGeneration;
	int loginNavigationFailures;
	string pendingCustomerUrl = "";
	bool pendingCustomerRetried;

	public void OnAppDeactivated()
	{
		deactivatedAt ??= sessionClock.GetUtcNow();
		appInactive = true;
		// Invalidate DOM reads that were running when the WebView was suspended.
		navigationGeneration++;
	}

	public void OnAppActivated()
	{
		var inactiveSince = deactivatedAt;
		deactivatedAt = null;
		appInactive = false;
		if (inactiveSince == null) return;
		if (sessionClock.GetUtcNow() - inactiveSince.Value >= ReLoginAfter
			|| loginPhase == LoginPhase.LoggingIn || bridgeActive || searchActive || detailActive || pendingCustomerUrl.Length > 0)
		{
			MarkRequestsForLogin(resetRetry: true);
			BeginReLogin(restart: true);
		}
	}

	bool EnsureSessionForRequest()
	{
		if (!appInactive && loginPhase == LoginPhase.LoggedIn) return true;
		MarkRequestsForLogin();
		if (!appInactive) BeginReLogin();
		return false;
	}

	void MarkRequestsForLogin(bool resetRetry = false)
	{
		if (resetRetry) pendingCustomerRetried = false;
		if (bridgeActive)
		{
			bridgeResume = true;
			if (resetRetry) bridgeResumeConsumed = false;
		}
		if (searchActive)
		{
			searchNeedsResume = true;
			if (resetRetry) searchResumedOnce = false;
		}
		if (detailActive)
		{
			detailNeedsResume = true;
			if (resetRetry) detailResumedOnce = false;
		}
	}

	bool TryRecoverSession()
	{
		if (appInactive) return true;
		var canRetry = (bridgeActive && !bridgeResumeConsumed)
			|| (searchActive && !searchResumedOnce)
			|| (detailActive && !detailResumedOnce)
			|| (pendingCustomerUrl.Length > 0 && !pendingCustomerRetried);
		if (!canRetry) return false;
		MarkRequestsForLogin();
		BeginReLogin();
		return true;
	}

	void HandleExpiredDocument()
	{
		if (!bridgeActive && !searchActive && !detailActive && pendingCustomerUrl.Length == 0
			&& loginPhase != LoginPhase.LoggingIn)
		{
			BeginReLogin();
			return;
		}
		if (TryRecoverSession()) return;
		loginPhase = LoginPhase.Failed;
		SetSessionStatus("Jシステム：ログイン失敗");
		FailPendingRequests();
	}

	void BeginReLogin(bool restart = false)
	{
		if (appInactive || (!restart && loginPhase == LoginPhase.LoggingIn)) return;
		sessionGeneration++;
		navigationGeneration++;
		loginAttempts = 0;
		loginNavigationFailures = 0;
		loginInProgress = false;
		autoLoginArmed = true;
		loginPhase = LoginPhase.LoggingIn;
		currentUrl = "";
		SetSessionStatus("Jシステム：再ログイン中...");
		EnsureWebViewHandler();
		ArmLoginTimeout();
		OpenUrl(LoginUrl, "再ログイン中");
	}

	bool TryRetryLoginNavigation()
	{
		if (++loginNavigationFailures > 2) return false;
		var generation = sessionGeneration;
		var navigation = navigationGeneration;
		Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), () =>
		{
			if (generation != sessionGeneration || navigation != navigationGeneration
				|| appInactive || loginPhase != LoginPhase.LoggingIn) return;
			OpenUrl(LoginUrl, "再ログイン中");
		});
		return true;
	}

	void ArmLoginTimeout()
	{
		var generation = ++loginTimeoutGeneration;
		Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(45), () =>
		{
			if (generation != loginTimeoutGeneration || loginPhase != LoginPhase.LoggingIn) return;
			if (appInactive) return; // Activation restarts authentication with a new timeout.
			loginPhase = LoginPhase.Failed;
			loginInProgress = false;
			sessionGeneration++;
			SetSessionStatus("Jシステム：ログイン失敗");
			FailPendingRequests();
		});
	}

	void FailPendingRequests()
	{
		ReportBridge(bridgeToken, "fail", null);
		ReportSearch(searchToken, "fail", null);
		ReportDetail(detailToken, "fail", null);
		pendingCustomerUrl = "";
		pendingCustomerRetried = false;
	}

	void ConfirmSession()
	{
		loginTimeoutGeneration++;
		loginAttempts = 0;
		loginInProgress = false;
		autoLoginArmed = true;
		loginPhase = LoginPhase.LoggedIn;
		FillStatusLabel.Text = "自動ログイン：成功";
		LoginStateLabel.Text = "ログイン状態：ログイン済み";
		SetSessionStatus("Jシステム：ログイン済み");
	}

	// A session can expire without redirecting: the login form or expiry message
	// may be returned at the customer/search URL. Never infer authentication from URL alone.
	const string DocumentSessionScript = """
		(function(){
		var nodes=document.querySelectorAll('input[type="password"]');
		for(var i=0;i<nodes.length;i++){
		  var el=nodes[i];
		  if(el&&(el.offsetParent!==null||el.getClientRects().length>0))return 'login';
		}
		var labels=document.querySelectorAll('th,td.infoHead,.infoHead');
		for(var j=0;j<labels.length;j++){
		  if(/^(受付No|伝票番号|お客様名|住所)[：:]?$/.test((labels[j].textContent||'').replace(/\s+/g,'')))return 'in';
		}
		if(document.getElementById('searchList'))return 'in';
		var text=(document.body?document.body.innerText:'').replace(/\s+/g,'');
		if(/セッション.{0,20}(切れ|切れて|有効期限|タイムアウト)|ログイン.{0,10}(有効期限|タイムアウト)|再度ログイン|再ログインしてください|ログインしてください|ログインが必要/.test(text))return 'login';
		return document.body?'in':'unknown';
		})()
		""";
}
