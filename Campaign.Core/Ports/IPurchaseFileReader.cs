namespace Campaign.Core.Ports;

/// <summary>
/// One line of a purchase report as it came out of the file. Every parsed field is optional and
/// <see cref="Error"/> says why when they are missing: reading the file is the adapter's job, and
/// deciding what a row means is the use case's.
/// </summary>
public sealed record PurchaseFileRow(
    int RowNumber,
    string RawLine,
    string? CustomerExternalId,
    DateOnly? PurchaseDate,
    decimal? Amount,
    string? Currency,
    string? Error);

/// <summary>
/// Reads a purchase report. A row that cannot be read comes back with an <see cref="PurchaseFileRow.Error"/>
/// and never stops the file; only a file that is not a report at all - empty, not a CSV, missing a
/// required column - is refused, as csv-invalid.
/// </summary>
public interface IPurchaseFileReader
{
    IAsyncEnumerable<PurchaseFileRow> ReadAsync(Stream content, CancellationToken ct);
}
