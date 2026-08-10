namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

public sealed record ImportBatchView(ImportBatch Batch, IReadOnlyList<PurchaseResult> Rows);

/// <summary>
/// The status of one import, with the rows it produced. Every row is reported, including the ones
/// that matched nothing, because an unmatched purchase is a result and not a failure to hide.
/// </summary>
public sealed class GetImportBatch
{
    private readonly IImportRepository _imports;

    public GetImportBatch(IImportRepository imports)
    {
        _imports = imports;
    }

    public async Task<ImportBatchView> ExecuteAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _imports.FindByIdAsync(batchId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.ImportBatchNotFound,
                "Unknown import batch.");

        return new ImportBatchView(batch, await _imports.ListResultsAsync(batchId, ct));
    }
}
