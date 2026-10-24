using RegulusMobile.Services;

static class EmergencyCustomerTests
{
	public static void Run()
	{
		// Same label/whitespace structure as the supplied October 24 sample;
		// personal and case data are replaced with fictional test values.
		const string sample = "登録者名：試験担当  【緊急受付 詳細データ】 伝票番号：123456789 顧客ID： お客様名：試験 太郎 部課名： 担当者名： ふりがな：シケン タロウ 受付店CD：1234 受付店名：試 験 店 受付者名：試験 花子 郵便番号：1234567 住所：試験県試験市1 電話番号1：00-0000-0000 電話番号2：000-0000-0000 訪問予定日： 依頼日：2026.10.22 依頼内容：【依頼プラン】 訪問基本料+オプションメニュー 【使用機器】 パソコン：試験機 プリンタ：試験機 ルータ：不明(無線) 【環境確認】 ISP書類の有無：未確認 プロバイダ：不明 回線種別：光ファイバー 回線開通済 【連絡・特記事項】 ☆回収代行:集金額8,980円☆ プリンタ設定をお願いします。\t\t\t 【依頼補足】\t\t\t 契約書および委託料金表に基づきます。 約束事項：【商品到着日】 到着済 【訪問希望日】 指示なし 終日OK 【打ち合わせ】 本日2026.10.22(木) 10:00～19:00 【連絡先】 ☆000-0000-0000☆\t\t\t 【約束補足】\t\t\t ◆◆要打ち合わせ◆◆ 最短での訪問希望です。 訪問日状況： ID：12345678";
		Check(EmergencyCustomerParser.TryParse(sample, "12345678", out var fields), "Parse the sample format");
		Check(fields["customerName"] == "試験 太郎", "Do not include empty department fields in the name");
		Check(fields["furigana"] == "シケン タロウ", "Hiragana label");
		Check(fields["storeCode"] == "1234" && fields["storeName"] == "試 験 店", "Store code and name");
		Check(fields["receptionistName"] == "試験 花子", "Receptionist");
		Check(fields["address"] == "〒123-4567\n試験県試験市1", "Postal code and address");
		Check(fields["phone1"] == "00-0000-0000" && fields["phone2"] == "000-0000-0000", "Phones remain separate");
		Check(fields["receptionNo"] == "12345678" && fields["slipNo"] == "123456789", "ID and slip number");
		Check(!fields.ContainsKey("visitScheduled") && fields["requestDate"] == "2026.10.22", "Empty visit date");
		Check(fields["requestDetails"].StartsWith("【依頼プラン】") && fields["requestDetails"].EndsWith("お願いします。"), "Keep nested request headings");
		Check(fields["requestSupplement"] == "契約書および委託料金表に基づきます。", "Bracketed request supplement");
		Check(fields["promiseDetails"].Contains("【連絡先】 ☆000-0000-0000☆"), "Contact heading belongs to promise text");
		Check(fields["promiseSupplement"] == "◆◆要打ち合わせ◆◆ 最短での訪問希望です。", "Exclude visit status and trailing ID");
		Check(!fields.ContainsKey("chargeTotal") && !fields.ContainsKey("hasDirectCollection"), "Do not infer charge details or direct collection");
		Check(!EmergencyCustomerParser.TryParse(sample, "87654321", out _), "Reject a different reception number");
		Check(!EmergencyCustomerParser.TryParse("受付番号：12345678", "12345678", out _), "A number alone is not a customer record");
		Check(!EmergencyCustomerParser.TryParse("", "", out _), "Missing second comment");
		Check(EmergencyCustomerParser.TryParse("お客様名\r\n試験 太郎\r\n住所：試験市\r\n依頼内容：1行目\r\n2行目\r\n約束事項：電話連絡", "12345678", out var multiline)
			&& multiline["requestDetails"] == "1行目\n2行目", "Preserve multiline body");
		foreach (var heading in new[] { "料金明細：", "明細情報：", "【料金明細】", "【明細情報】", "明細情報\n" })
		{
			Check(EmergencyCustomerParser.TryParse("お客様名：試験 太郎\n" + heading + "\n商品コード 商品名 単価 数量 金額\n001 訪問基本料 3,000 1 3,000\n002 設定料 2,000 2 4,000\n合計：7,000円\nID：12345678", "12345678", out var detail)
				&& detail["chargeDetails"] == "商品コード 商品名 単価 数量 金額\n001 訪問基本料 3,000 1 3,000\n002 設定料 2,000 2 4,000\n合計：7,000円"
				&& detail["customerName"] == "試験 太郎" && detail["receptionNo"] == "12345678", "Preserve detail rows and totals: " + heading);
		}
		Console.WriteLine("PASS: emergency customer parsing (23 checks)");
	}

	static void Check(bool condition, string message)
	{
		if (!condition) throw new Exception(message);
	}
}
