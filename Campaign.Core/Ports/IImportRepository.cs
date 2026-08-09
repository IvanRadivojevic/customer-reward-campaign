namespace Campaign.Core.Ports;

using Campaign.Core.Domain;

/// <summary>
/// Storage for purchase report imports. P-08 is handled by attempting the insert and reading the
/// existing batch back when the unique index on (CampaignId, FileSha256) rejects it, which is why
/// there is no "does it already exist" question before the write.
/// </summary>
public interface IImportRepository
{
    Task<ImportBatch?> FindByIdAsync(Guid batchId, CancellationToken ct);

    Task<ImportBatch?> FindByFileHashAsync(Guid campaignId, string fileSha256, CancellationToken ct);

    Task<IReadOnlyList<PurchaseResult>> ListResultsAsync(Guid batchId, CancellationToken ct);

    /// <summary>Stores the batch together with its rows; they are only ever written as one unit.</summary>
    Task AddAsync(ImportBatch batch, IReadOnlyCollection<PurchaseResult> results, CancellationToken ct);
}
