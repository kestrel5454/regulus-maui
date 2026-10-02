namespace RegulusMobile;

public partial class SearchPage : ContentPage
{
	static readonly Color Ink = Color.FromArgb("#0F172A");
	static readonly Color Muted = Color.FromArgb("#64748B");
	static readonly Color PageBg = Color.FromArgb("#EEF3F8");

	int searchRequest;
	int detailRequest;

	public SearchPage()
	{
		InitializeComponent();
	}

	void OnSearchClicked(object? sender, EventArgs e)
	{
		DismissKeyboard();
		var slip = (SlipEntry.Text ?? "").Trim();
		var name = (NameEntry.Text ?? "").Trim();
		var tel = (TelEntry.Text ?? "").Trim();
		if (slip.Length == 0 && name.Length == 0 && tel.Length == 0)
		{
			ShowSearchMessage("検索条件を入力してください");
			return;
		}
		var page = JSystemPage.Current;
		if (page == null)
		{
			ShowSearchMessage("検索結果取得失敗");
			return;
		}
		var token = ++searchRequest;
		detailRequest++;
		CustomerPanel.IsVisible = false;
		ShowSearchMessage("検索中...");
		page.BeginReceptionSearch(token, "", name, tel, "", slip, (done, status, hits) =>
		{
			if (done != searchRequest) return;
			if (status == "ok" && hits != null)
			{
				ShowHits(hits);
				return;
			}
			ShowSearchMessage("検索結果取得失敗");
		});
	}

	void DismissKeyboard()
	{
#if ANDROID
		var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
		var token = activity?.CurrentFocus?.WindowToken;
#endif
		NameEntry.Unfocus();
		SlipEntry.Unfocus();
		TelEntry.Unfocus();
#if ANDROID
		if (activity != null && token != null)
		{
			var manager = activity.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
			manager?.HideSoftInputFromWindow(token, Android.Views.InputMethods.HideSoftInputFlags.None);
		}
#endif
	}

	void OnClearClicked(object? sender, EventArgs e)
	{
		NameEntry.Text = "";
		SlipEntry.Text = "";
		TelEntry.Text = "";
		searchRequest++;
		detailRequest++;
		CustomerPanel.IsVisible = false;
		CustomerStack.Children.Clear();
		SearchStatusLabel.Text = "";
		ResultStack.Children.Clear();
	}

	void OnCloseCustomerClicked(object? sender, EventArgs e)
	{
		detailRequest++;
		CustomerPanel.IsVisible = false;
		CustomerStack.Children.Clear();
	}

	void ShowSearchMessage(string text)
	{
		SearchStatusLabel.Text = text;
		ResultStack.Children.Clear();
		if (text == "検索中...") return;
		ResultStack.Children.Add(new Label
		{
			Text = text,
			FontSize = 16,
			TextColor = Ink,
			BackgroundColor = PageBg,
			Margin = new Thickness(0, 12, 0, 0)
		});
	}

	void ShowHits(IReadOnlyList<JSystemPage.SearchHit> hits)
	{
		SearchStatusLabel.Text = hits.Count + "件";
		ResultStack.Children.Clear();
		if (hits.Count == 0)
		{
			ResultStack.Children.Add(new Label
			{
				Text = "検索結果はありません",
				FontSize = 16,
				TextColor = Ink,
				BackgroundColor = PageBg
			});
			return;
		}
		foreach (var hit in hits) ResultStack.Children.Add(MakeHitCard(hit));
	}

	View MakeHitCard(JSystemPage.SearchHit hit)
	{
		var canOpen = IsReceptionNumber(hit.ReceptionNo)
			|| hit.DetailPath.Contains("history_request_sub.php", StringComparison.OrdinalIgnoreCase);
		var body = new Grid
		{
			Padding = new Thickness(10, 5),
			RowSpacing = 0,
			ColumnSpacing = 8,
			BackgroundColor = Color.FromArgb("#F8FBFE"),
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto)
			},
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			}
		};
		var name = MakeNameLine(hit.CustomerName, hit.Furigana);
		body.Add(name, 0, 0);
		Grid.SetColumnSpan(name, 2);
		var plan = new Label
		{
			Text = Shown(hit.PlanName),
			FontSize = 14,
			TextColor = Ink,
			LineBreakMode = LineBreakMode.TailTruncation
		};
		body.Add(plan, 0, 1);
		Grid.SetColumnSpan(plan, 2);
		body.Add(new Label
		{
			Text = "受付No " + Shown(hit.ReceptionNo) + "   伝票 " + FormatSlipNumber(hit.SlipNo) + "   完了 " + DisplayDate(hit.CompletedDate),
			FontSize = 13,
			TextColor = Muted,
			VerticalOptions = LayoutOptions.Center,
			LineBreakMode = LineBreakMode.TailTruncation
		}, 0, 2);
		var button = new Button
		{
			Text = "詳細",
			TextColor = Colors.White,
			BackgroundColor = Color.FromArgb("#0E5A8A"),
			FontSize = 13,
			Padding = new Thickness(10, 0),
			HeightRequest = 32,
			IsEnabled = canOpen,
			VerticalOptions = LayoutOptions.Center
		};
		button.Clicked += (_, _) => OpenDetail(hit);
		body.Add(button, 1, 2);
		return new Border
		{
			BackgroundColor = Color.FromArgb("#F8FBFE"),
			Stroke = Color.FromArgb("#D5E2EE"),
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Padding = 0,
			Content = body
		};
	}

	void OpenDetail(JSystemPage.SearchHit hit)
	{
		var page = JSystemPage.Current;
		var hasReceptionNumber = IsReceptionNumber(hit.ReceptionNo);
		var hasArchivePath = hit.DetailPath.Contains("history_request_sub.php", StringComparison.OrdinalIgnoreCase);
		if (page == null || (!hasReceptionNumber && !hasArchivePath))
		{
			SearchStatusLabel.Text = "詳細を開けません";
			return;
		}
		var token = ++detailRequest;
		ShowCustomerMessage("顧客情報を取得中...");
		Action<int, string, JSystemPage.CustomerCard?> showResult = (done, status, card) =>
		{
			if (done != detailRequest) return;
			if (status == "ok" && card != null)
			{
				CustomerStack.Children.Clear();
				CustomerStack.Children.Add(page.CreateSharedCustomerView(card));
				CustomerPanel.IsVisible = true;
				return;
			}
			ShowCustomerMessage("顧客情報取得失敗");
		};
		if (hasReceptionNumber)
			page.ReadFromRegulus(hit.ReceptionNo, token, showResult);
		else
			page.OpenHistoryDetail(token, hit.DetailPath, showResult);
	}

	static bool IsReceptionNumber(string value)
	{
		return value.Length == 8 && value.All(char.IsDigit);
	}

	static string FormatSlipNumber(string? value)
	{
		var text = (value ?? "").Trim();
		if (text.Length <= 3 || !text.All(char.IsDigit)) return text.Length == 0 ? "---" : text;
		var parts = new List<string>();
		for (var index = 0; index < text.Length; index += 3)
			parts.Add(text.Substring(index, Math.Min(3, text.Length - index)));
		return string.Join(" ", parts);
	}

	void ShowCustomerMessage(string text)
	{
		CustomerPanel.IsVisible = true;
		CustomerStack.Children.Clear();
		CustomerStack.Children.Add(new Label
		{
			Text = text,
			FontSize = 18,
			TextColor = Ink,
			BackgroundColor = PageBg,
			HorizontalTextAlignment = TextAlignment.Center,
			Margin = new Thickness(0, 72, 0, 0)
		});
	}

	static View MakeNameLine(string name, string furigana)
	{
		var line = new FlexLayout
		{
			Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
			Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
			AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
			JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start
		};
		line.Children.Add(new Label
		{
			Text = Honorific(name),
			FontSize = 16,
			FontAttributes = FontAttributes.Bold,
			TextColor = Ink,
			Margin = new Thickness(0, 0, 8, 0)
		});
		if (HasText(furigana))
		{
			line.Children.Add(new Label
			{
				Text = furigana.Trim(),
				FontSize = 13,
				TextColor = Muted
			});
		}
		return line;
	}

	static string Honorific(string value)
	{
		var shown = Shown(value);
		if (shown == "---" || shown.EndsWith("様", StringComparison.Ordinal)) return shown;
		return shown + " 様";
	}

	static string DisplayDate(string value)
	{
		var shown = Shown(value);
		if (shown == "---") return shown;
		return shown.Replace('.', '/');
	}

	static string Shown(string? value)
	{
		var text = (value ?? "").Trim();
		if (text.Length == 0 || text == "---" || text.Equals("null", StringComparison.OrdinalIgnoreCase)) return "---";
		return text;
	}

	static bool HasText(string? value)
	{
		return Shown(value) != "---";
	}
}
