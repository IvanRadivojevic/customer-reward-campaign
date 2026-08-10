namespace Campaign.Infrastructure.Persistence;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Reads the report straight out of the view. Nothing is counted in C# here, which is the point: the
/// arithmetic exists once, in SQL, where a reporting tool can reach it too.
/// </summary>
public sealed class EfReportRepository : IReportRepository
{
    private readonly AppDbContext _db;

    public EfReportRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CampaignResultRow>> ReadResultsAsync(Guid campaignId, CancellationToken ct) =>
        await _db.CampaignResults
            .Where(row => row.CampaignId == campaignId)
            .ToListAsync(ct);

    /// <summary>
    /// The campaign is reached through the batch, because a purchase row carries no campaign of its
    /// own - it belongs to the file it arrived in.
    /// </summary>
    public async Task<IReadOnlyList<PurchaseResult>> ListUnmatchedAsync(Guid campaignId, CancellationToken ct) =>
        await (from row in _db.PurchaseResults
               join batch in _db.ImportBatches on row.BatchId equals batch.Id
               where batch.CampaignId == campaignId && row.MatchStatus == MatchStatus.Unmatched
               orderby batch.UploadedAtUtc, row.RowNumber
               select row)
            .ToListAsync(ct);
}
