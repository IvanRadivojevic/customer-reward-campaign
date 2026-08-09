namespace Campaign.Infrastructure.Persistence;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Microsoft.EntityFrameworkCore;

public sealed class EfImportRepository : IImportRepository
{
    private readonly AppDbContext _db;

    public EfImportRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<ImportBatch?> FindByIdAsync(Guid batchId, CancellationToken ct) =>
        _db.ImportBatches.FirstOrDefaultAsync(batch => batch.Id == batchId, ct);

    public Task<ImportBatch?> FindByFileHashAsync(Guid campaignId, string fileSha256, CancellationToken ct) =>
        _db.ImportBatches.FirstOrDefaultAsync(
            batch => batch.CampaignId == campaignId && batch.FileSha256 == fileSha256,
            ct);

    public async Task<IReadOnlyList<PurchaseResult>> ListResultsAsync(Guid batchId, CancellationToken ct) =>
        await _db.PurchaseResults
            .Where(row => row.BatchId == batchId)
            .OrderBy(row => row.RowNumber)
            .ToListAsync(ct);

    public async Task AddAsync(ImportBatch batch, IReadOnlyCollection<PurchaseResult> results, CancellationToken ct)
    {
        await _db.ImportBatches.AddAsync(batch, ct);
        await _db.PurchaseResults.AddRangeAsync(results, ct);
    }
}
