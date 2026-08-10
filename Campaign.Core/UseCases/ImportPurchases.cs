namespace Campaign.Core.UseCases;

using Campaign.Core.Domain;
using Campaign.Core.Ports;

public sealed record ImportPurchasesCommand(
    Guid CampaignId,
    string FileName,
    string FileSha256,
    string UploadedBy,
    Stream Content);

/// <summary>
/// <see cref="AlreadyImported"/> is true when this exact file had already been imported, so the
/// caller can tell a fresh import from the answer P-08 gives to a repeat.
/// </summary>
public sealed record ImportPurchasesResult(ImportBatch Batch, bool AlreadyImported);

/// <summary>
/// Reads a purchase report and matches every row against the grants of one campaign. Processing is
/// row by row and tolerant: a row that cannot be read is stored with its reason and its raw line, and
/// the file is always finished. The CSV is the authority on who bought - there is no check that the
/// purchase happened after the grant, because a purchase date is data, not a rule.
/// </summary>
public sealed class ImportPurchases
{
    private readonly IImportRepository _imports;
    private readonly IGrantRepository _grants;
    private readonly IPurchaseFileReader _reader;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BusinessDateProvider _businessDates;

    public ImportPurchases(
        IImportRepository imports,
        IGrantRepository grants,
        IPurchaseFileReader reader,
        IUnitOfWork unitOfWork,
        BusinessDateProvider businessDates)
    {
        _imports = imports;
        _grants = grants;
        _reader = reader;
        _unitOfWork = unitOfWork;
        _businessDates = businessDates;
    }

    public async Task<ImportPurchasesResult> ExecuteAsync(ImportPurchasesCommand command, CancellationToken ct)
    {
        // The campaign has to exist, but it does not have to be open: the report arrives a month
        // after the campaign ends, which is the whole point of the exercise.
        var campaign = await _grants.FindCampaignAsync(command.CampaignId, ct)
            ?? throw new DomainRuleViolationException(
                DomainErrorCodes.CampaignNotActive,
                "The campaign does not exist.");

        // The active grants of this campaign, read once. A row asks this dictionary rather than the
        // database, so a file with ten thousand rows is still one query.
        var activeGrants = await _grants.ListAsync(
            new GrantQuery(campaign.Id, Status: GrantStatus.Active),
            ct);

        var grantsByCustomer = activeGrants.ToDictionary(
            grant => grant.CustomerExternalId,
            grant => grant.Id,
            StringComparer.Ordinal);

        var batchId = Guid.NewGuid();
        var results = new List<PurchaseResult>();

        await foreach (var row in _reader.ReadAsync(command.Content, ct))
        {
            results.Add(Interpret(batchId, row, grantsByCustomer));
        }

        var batch = ImportBatch.Create(
            batchId,
            campaign.Id,
            command.FileName,
            command.FileSha256,
            _businessDates.UtcNow(),
            command.UploadedBy,
            rowsTotal: results.Count,
            rowsMatched: results.Count(result => result.MatchStatus == MatchStatus.Matched),
            rowsUnmatched: results.Count(result => result.MatchStatus == MatchStatus.Unmatched),
            rowsInvalid: results.Count(result => result.MatchStatus == MatchStatus.Invalid));

        try
        {
            await _imports.AddAsync(batch, results, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return new ImportPurchasesResult(batch, AlreadyImported: false);
        }
        catch (DuplicateImportBatchException)
        {
            // P-08. Somebody imported this exact file first - possibly a millisecond ago. Their batch
            // is the answer; this one is thrown away.
            var existing = await _imports.FindByFileHashAsync(campaign.Id, command.FileSha256, ct)
                ?? throw new DomainRuleViolationException(
                    DomainErrorCodes.CsvInvalid,
                    "This file has already been imported, but the earlier batch could not be read back.");

            return new ImportPurchasesResult(existing, AlreadyImported: true);
        }
    }

    /// <summary>
    /// The three outcomes from SPEC section 8. A customer may appear more than once and every one of
    /// those rows is matched: without an order identifier in the file there is no way to tell a
    /// duplicated line from a second purchase, so no status is invented for it.
    /// </summary>
    private static PurchaseResult Interpret(
        Guid batchId,
        PurchaseFileRow row,
        IReadOnlyDictionary<string, Guid> grantsByCustomer)
    {
        if (row.Error is not null)
        {
            return PurchaseResult.Invalid(Guid.NewGuid(), batchId, row.RowNumber, row.RawLine, row.Error);
        }

        if (string.IsNullOrWhiteSpace(row.CustomerExternalId) || row.PurchaseDate is null)
        {
            return PurchaseResult.Invalid(
                Guid.NewGuid(),
                batchId,
                row.RowNumber,
                row.RawLine,
                "CustomerId and PurchaseDate are both required.");
        }

        if (grantsByCustomer.TryGetValue(row.CustomerExternalId, out var grantId))
        {
            return PurchaseResult.Matched(
                Guid.NewGuid(),
                batchId,
                row.RowNumber,
                row.RawLine,
                row.CustomerExternalId,
                row.PurchaseDate.Value,
                row.Amount,
                row.Currency,
                grantId);
        }

        // No active grant, which includes the case where the grant was voided. Reported, never
        // dropped in silence.
        return PurchaseResult.Unmatched(
            Guid.NewGuid(),
            batchId,
            row.RowNumber,
            row.RawLine,
            row.CustomerExternalId,
            row.PurchaseDate.Value,
            row.Amount,
            row.Currency);
    }
}
