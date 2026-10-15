namespace RegulusMobile;

public partial class JSystemPage
{
	// Process memory only: never serialize customer details to device storage.
	readonly Dictionary<string, CustomerCard> customerMemoryCache = new(StringComparer.Ordinal);

	public bool TryGetCachedCustomer(string receptionNo, out CustomerCard? card)
		=> customerMemoryCache.TryGetValue(receptionNo, out card);
}
