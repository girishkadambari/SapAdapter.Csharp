namespace SapAdapter.Snapshot.Packs;

/// <summary>
/// Interface for transaction-specific screen packs that enrich snapshots
/// with domain-specific entity extraction (PO, Vendor, Invoice, etc.)
/// </summary>
public interface IScreenPack
{
    /// <summary>Check if this pack matches the current snapshot transaction/screen.</summary>
    bool Match(Models.SapScreenSnapshot snapshot);

    /// <summary>Apply domain extraction to enrich the snapshot with entities.</summary>
    void Apply(ExtractorContext ctx, Models.SapScreenSnapshot snapshot);
}
