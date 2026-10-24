using System.Text.Json;

namespace RegulusMobile;

public partial class JSystemPage
{
	public View CreateEmergencyCustomerView(Dictionary<string, string> fields)
	{
		using var document = JsonDocument.Parse(JsonSerializer.Serialize(fields));
		var view = (VerticalStackLayout)CreateSharedCustomerView(BindCustomer(document.RootElement), includeCharges: false);
		if (fields.TryGetValue("chargeDetails", out var details) && HasDisplayValue(details))
			view.Children.Insert(3, MakeBandCard("明細情報", BandCharge, BodyCharge,
				MakeLongField("料金明細", details)));
		return view;
	}
}
