namespace Campaign.Infrastructure.Import;

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Campaign.Core.Domain;
using Campaign.Core.Ports;
using CsvHelper;
using CsvHelper.Configuration;

/// <summary>
/// Column names as the file spells them. Configurable through Import:ColumnMap, because the operator
/// sending the report is not going to rename their columns for us.
/// </summary>
public sealed class ImportColumnMap
{
    public string CustomerId { get; init; } = "CustomerId";

    public string PurchaseDate { get; init; } = "PurchaseDate";

    public string Amount { get; init; } = "Amount";

    public string Currency { get; init; } = "Currency";
}

/// <summary>
/// Reads the purchase report with CsvHelper. Only two things stop the file: it is empty, or it does
/// not carry the two required columns. Everything else is a row-level problem, and a row-level
/// problem is reported on that row and nowhere else.
/// </summary>
public sealed class CsvPurchaseFileReader : IPurchaseFileReader
{
    private readonly ImportColumnMap _columns;

    public CsvPurchaseFileReader(ImportColumnMap columns)
    {
        _columns = columns;
    }

    public async IAsyncEnumerable<PurchaseFileRow> ReadAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // The separator is either a comma or a semicolon, and the file says which.
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";"],
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null
        };

        using var csv = new CsvReader(reader, configuration);

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.CsvInvalid,
                "The file is empty or carries no header row.");
        }

        RequireColumn(csv, _columns.CustomerId);
        RequireColumn(csv, _columns.PurchaseDate);

        var rowNumber = 0;

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;

            yield return Read(csv, rowNumber);
        }
    }

    private void RequireColumn(CsvReader csv, string name)
    {
        if (!csv.HeaderRecord?.Contains(name, StringComparer.OrdinalIgnoreCase) ?? true)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.CsvInvalid,
                $"The file has no '{name}' column. Configure Import:ColumnMap if it is named differently.");
        }
    }

    private PurchaseFileRow Read(CsvReader csv, int rowNumber)
    {
        // The raw line is kept whatever happens to the parsed fields: for a row that could not be
        // read it is the only record of what actually arrived.
        var rawLine = csv.Parser.RawRecord.TrimEnd('\r', '\n');

        var customerId = Field(csv, _columns.CustomerId);
        var purchaseDateText = Field(csv, _columns.PurchaseDate);
        var amountText = Field(csv, _columns.Amount);
        var currency = Field(csv, _columns.Currency);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Invalid(rowNumber, rawLine, "CustomerId is empty.");
        }

        // The file is somebody else's output, so a field can be any length. A value the column cannot
        // hold has to become an invalid row here; letting it through would fail the whole insert and
        // lose every good row in the file with it.
        if (customerId.Length > PurchaseResult.MaxCustomerExternalIdLength)
        {
            return Invalid(
                rowNumber,
                rawLine,
                $"CustomerId is longer than the {PurchaseResult.MaxCustomerExternalIdLength} characters allowed.");
        }

        if (!DateOnly.TryParse(purchaseDateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var purchaseDate))
        {
            return Invalid(rowNumber, rawLine, $"PurchaseDate '{purchaseDateText}' is not a date.");
        }

        decimal? amount = null;
        if (!string.IsNullOrWhiteSpace(amountText))
        {
            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return Invalid(rowNumber, rawLine, $"Amount '{amountText}' is not a number.");
            }

            // A decimal holds far more than decimal(18,2) does, so a number can parse and still be
            // impossible to store.
            if (parsed > PurchaseResult.MaxAmount || parsed < PurchaseResult.MinAmount)
            {
                return Invalid(rowNumber, rawLine, $"Amount '{amountText}' is too large to store.");
            }

            amount = parsed;
        }

        // Amount and currency are optional, but they travel as a pair: an amount without a currency
        // is ambiguous data, and the database refuses it as well.
        if (amount.HasValue != !string.IsNullOrWhiteSpace(currency))
        {
            return Invalid(rowNumber, rawLine, "Amount and currency have to be either both present or both absent.");
        }

        if (!string.IsNullOrWhiteSpace(currency) && currency.Length != PurchaseResult.CurrencyCodeLength)
        {
            return Invalid(rowNumber, rawLine, $"Currency '{currency}' is not a three letter code.");
        }

        return new PurchaseFileRow(
            rowNumber,
            rawLine,
            customerId,
            purchaseDate,
            amount,
            string.IsNullOrWhiteSpace(currency) ? null : currency,
            Error: null);
    }

    /// <summary>
    /// The reason quotes what the file contained, and that has no length limit, so it is shortened to
    /// what the column takes. A truncated reason is still a reason; a row lost to an oversized one
    /// would take the whole import with it.
    /// </summary>
    private static PurchaseFileRow Invalid(int rowNumber, string rawLine, string error) =>
        new(
            rowNumber,
            rawLine,
            null,
            null,
            null,
            null,
            error.Length > PurchaseResult.MaxErrorLength ? error[..PurchaseResult.MaxErrorLength] : error);

    private static string? Field(CsvReader csv, string name) =>
        csv.TryGetField<string>(name, out var value) ? value : null;
}
