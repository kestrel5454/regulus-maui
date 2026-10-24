using RegulusMobile;

JSystemPage.RunRegressionTests();
EmergencyCustomerTests.Run();

namespace RegulusMobile
{
	// Headless effects for the production session partial: no credentials, network,
	// WebView, or real clock are needed to exercise lifecycle and retry boundaries.
	public partial class JSystemPage
	{
		enum LoginPhase { NotStarted, LoggingIn, LoggedIn, Failed, CredentialsMissing }
		const string LoginUrl = "login";
		LoginPhase loginPhase = LoginPhase.LoggedIn;
		bool bridgeActive, bridgeResume, bridgeResumeConsumed;
		bool searchActive, searchNeedsResume, searchResumedOnce;
		bool detailActive, detailNeedsResume, detailResumedOnce;
		bool loginInProgress, autoLoginArmed;
		int loginAttempts;
		int bridgeToken = 1, searchToken = 2, detailToken = 3;
		string currentUrl = "customer";
		readonly FakeDispatcher Dispatcher = new();
		readonly FakeLabel FillStatusLabel = new(), LoginStateLabel = new();
		readonly List<string> navigations = [];
		int failures;
		void SetSessionStatus(string text) { }
		void EnsureWebViewHandler() { }
		void OpenUrl(string url, string state) => navigations.Add(url);
		void ReportBridge(int token, string state, object? card) { if (bridgeActive) failures++; bridgeActive = false; }
		void ReportSearch(int token, string state, object? hits) { if (searchActive) failures++; searchActive = false; }
		void ReportDetail(int token, string state, object? card) { if (detailActive) failures++; detailActive = false; }

		static JSystemPage New(out FakeClock clock)
		{
			clock = new FakeClock();
			return new JSystemPage { sessionClock = clock };
		}

		static void Check(bool value, string message)
		{
			if (!value) throw new Exception(message);
		}

		public static void RunRegressionTests()
		{
			var page = New(out var clock);
			page.OnAppDeactivated();
			clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
			page.OnAppActivated();
			Check(page.navigations.Count == 0, "Under ten minutes should retain the session");

			page = New(out clock);
			page.OnAppDeactivated();
			clock.Advance(TimeSpan.FromMinutes(5));
			page.OnAppDeactivated(); // Stopped must not reset the Deactivated timestamp.
			clock.Advance(TimeSpan.FromMinutes(5));
			page.OnAppActivated();
			page.OnAppActivated(); // Resumed + Activated must not start two logins.
			Check(page.navigations.Count == 1 && page.loginPhase == LoginPhase.LoggingIn,
				"Exactly ten minutes must trigger one login, including duplicate lifecycle events");
			page.bridgeActive = true;
			Check(!page.EnsureSessionForRequest() && page.bridgeResume && page.failures == 0,
				"A detail requested during login must wait without reporting an error");
			Check(page.navigations.Count == 1, "A pending request must share the in-flight login");
			page.ConfirmSession();
			page.Dispatcher.RunAll(); // The old login timeout must no longer apply.
			Check(page.EnsureSessionForRequest() && page.failures == 0,
				"Successful login must release the request and cancel its login timeout");

			page = New(out clock);
			page.bridgeActive = true;
			Check(page.TryRecoverSession() && page.bridgeResume && page.failures == 0,
				"An expired session must preserve the original customer request");
			page.ConfirmSession();
			page.bridgeResumeConsumed = true; // The request has been replayed.
			Check(!page.TryRecoverSession(), "A repeated failure must not loop through logins forever");

			page = New(out clock);
			page.loginPhase = LoginPhase.Failed;
			page.searchActive = true;
			Check(!page.EnsureSessionForRequest() && page.searchNeedsResume && page.failures == 0,
				"Search must start authentication instead of failing from a stale failed state");
			page.Dispatcher.RunAll();
			Check(page.loginPhase == LoginPhase.Failed && page.failures == 1,
				"An unresponsive login must terminate and release the pending request");

			page = New(out clock);
			page.detailActive = true;
			page.OnAppDeactivated();
			page.OnAppActivated();
			Check(page.detailNeedsResume && page.navigations.Count == 1 && page.failures == 0,
				"A read interrupted by a short sleep must be recovered too");

			page = New(out clock);
			page.bridgeActive = true;
			page.BeginReLogin();
			page.OnAppDeactivated();
			page.Dispatcher.RunAll();
			Check(page.failures == 0, "Timeouts during sleep must not surface errors");
			page.OnAppActivated();
			Check(page.navigations.Count == 2 && page.bridgeResume,
				"An interrupted login must restart with its request preserved");

			page = New(out clock);
			page.HandleExpiredDocument();
			Check(page.navigations.Count == 1,
				"A login form served at a protected URL must also renew an idle session");

			page = New(out clock);
			page.BeginReLogin();
			Check(page.TryRetryLoginNavigation(), "A transient wake-up network error should retry");
			page.Dispatcher.RunLast();
			Check(page.navigations.Count == 2 && page.failures == 0,
				"Retry must navigate again without reporting a preliminary error");
			Check(page.TryRetryLoginNavigation() && !page.TryRetryLoginNavigation(),
				"Network recovery attempts must be bounded");
			page.ConfirmSession();
			page.Dispatcher.RunAll();
			Check(page.navigations.Count == 2,
				"A delayed retry must not replace a successfully authenticated page");

			page = New(out clock);
			page.loginPhase = LoginPhase.CredentialsMissing;
			page.detailActive = true;
			page.OnAppDeactivated();
			Check(!page.EnsureSessionForRequest() && page.navigations.Count == 0 && page.detailNeedsResume,
				"Requests must not navigate a suspended WebView");

			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "document-session.js"), DocumentSessionScript);
			Console.WriteLine("PASS: session renewal, lifecycle boundaries, pending requests, bounded retries, and login timeouts");
		}
	}

	sealed class FakeClock : TimeProvider
	{
		DateTimeOffset now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => now;
		public void Advance(TimeSpan elapsed) => now += elapsed;
	}
	sealed class FakeLabel { public string Text { get; set; } = ""; }
	sealed class FakeDispatcher
	{
		readonly List<Action> actions = [];
		public void DispatchDelayed(TimeSpan delay, Action action) => actions.Add(action);
		public void RunLast()
		{
			var action = actions[^1];
			actions.RemoveAt(actions.Count - 1);
			action();
		}
		public void RunAll()
		{
			var pending = actions.ToArray();
			actions.Clear();
			foreach (var action in pending) action();
		}
	}
}
