using System.Text.RegularExpressions;

namespace RegulusMobile.Services;

// Parses labels copied from the customer screen, preserving multiline values.
// Input must be the second comment body, never the combined comment history.
public static class EmergencyCustomerParser
{
	static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
	{
		["受付No"] = "receptionNo", ["受付番号"] = "receptionNo", ["受付NO."] = "receptionNo",
		["ID"] = "receptionNo",
		["伝票番号"] = "slipNo", ["受付店"] = "storeName", ["受付店コード"] = "storeCode",
		["受付者名"] = "receptionistName", ["受付者"] = "receptionistName",
		["受付店CD"] = "storeCode", ["受付店名"] = "storeName", ["ふりがな"] = "furigana",
		["郵便番号"] = "postalCode", ["訪問予定日"] = "visitScheduled",
		["登録者名"] = "", ["顧客ID"] = "", ["部課名"] = "", ["担当者名"] = "", ["訪問日状況"] = "",
		["お客様名"] = "customerName", ["顧客名"] = "customerName", ["フリガナ"] = "furigana",
		["住所"] = "address", ["電話①"] = "phone1", ["電話1"] = "phone1", ["電話番号1"] = "phone1",
		["電話②"] = "phone2", ["電話2"] = "phone2", ["電話番号2"] = "phone2", ["電話番号"] = "phone1",
		["訪問確定"] = "visitConfirmed", ["訪問確定日"] = "visitConfirmed", ["作業担当"] = "worker",
		["訪問希望"] = "visitRequested", ["依頼先使用欄"] = "requestUseField", ["依頼日"] = "requestDate",
		["訪問予定"] = "visitScheduled", ["依頼内容"] = "requestDetails", ["約束事項"] = "promiseDetails",
		["依頼補足"] = "requestSupplement", ["約束補足"] = "promiseSupplement", ["契約書No"] = "contractNo",
		// Keep the supplied detail text intact; do not infer amounts from request prose.
		["料金明細"] = "chargeDetails", ["明細情報"] = "chargeDetails", ["明細"] = "chargeDetails"
	};
	static readonly Regex Label = new(
		@"(?<!\S)(?:(?<label>" +
		string.Join("|", Fields.Keys.OrderByDescending(key => key.Length).Select(Regex.Escape)) +
		@")[ 　]*[：:][ 　]*|【(?<supplement>依頼補足|約束補足|料金明細|明細情報|明細)】[ \t　]*|(?<line>" +
		string.Join("|", Fields.Keys.OrderByDescending(key => key.Length).Select(Regex.Escape)) +
		@")[ \t　]*(?=\n|$))",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	public static bool TryParse(string text, string receptionNo, out Dictionary<string, string> fields)
	{
		fields = new(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(text) || text.Length > 60000) return false;
		text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		var labels = Label.Matches(text);
		for (var i = 0; i < labels.Count; i++)
		{
			var match = labels[i];
			var label = match.Groups["label"].Success ? match.Groups["label"].Value
				: match.Groups["supplement"].Success ? match.Groups["supplement"].Value : match.Groups["line"].Value;
			var key = Fields[label];
			if (key.Length == 0) continue;
			var end = i + 1 < labels.Count ? labels[i + 1].Index : text.Length;
			var value = text[(match.Index + match.Length)..end].Trim();
			if (value.Length > 0)
				fields[key] = fields.TryGetValue(key, out var previous) ? previous + "\n" + value : value;
		}
		// A reception number alone is not customer detail.
		if (!fields.ContainsKey("customerName") && !fields.ContainsKey("address")
			&& !fields.ContainsKey("phone1") && !fields.ContainsKey("requestDetails")) return false;
		if (fields.TryGetValue("receptionNo", out var parsed))
		{
			var number = Regex.Replace(parsed.Normalize(System.Text.NormalizationForm.FormKC), @"[\s-]", "");
			if (!Regex.IsMatch(number, @"\A[0-9]{8}\z")) return false;
			if (receptionNo.Length > 0 && number != receptionNo) return false;
			fields["receptionNo"] = number;
		}
		else fields["receptionNo"] = receptionNo;
		if (fields.Remove("postalCode", out var postal) && fields.TryGetValue("address", out var address))
		{
			postal = Regex.Replace(postal, @"\D", "");
			if (postal.Length == 7) fields["address"] = "〒" + postal[..3] + "-" + postal[3..] + "\n" + address;
		}
		return true;
	}
}
