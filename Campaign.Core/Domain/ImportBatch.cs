namespace Campaign.Core.Domain;

/// <summary>
/// One processed purchase report. The SHA-256 of the file makes a repeated upload of the same file
/// in the same campaign idempotent (P-08).
/// </summary>
public sealed class ImportBatch
{
    public const int Sha256HexLength = 64;

    private ImportBatch(
        Guid id,
        Guid campaignId,
        string fileName,
        string fileSha256,
        DateTimeOffset uploadedAtUtc,
        string uploadedBy,
        int rowsTotal,
        int rowsMatched,
        int rowsUnmatched,
        int rowsInvalid,
        ImportBatchStatus status)
    {
        Id = id;
        CampaignId = campaignId;
        FileName = fileName;
        FileSha256 = fileSha256;
        UploadedAtUtc = uploadedAtUtc;
        UploadedBy = uploadedBy;
        RowsTotal = rowsTotal;
        RowsMatched = rowsMatched;
        RowsUnmatched = rowsUnmatched;
        RowsInvalid = rowsInvalid;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid CampaignId { get; private set; }

    public string FileName { get; private set; }

    public string FileSha256 { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public string UploadedBy { get; private set; }

    public int RowsTotal { get; private set; }

    public int RowsMatched { get; private set; }

    public int RowsUnmatched { get; private set; }

    public int RowsInvalid { get; private set; }

    public ImportBatchStatus Status { get; private set; }

    public static ImportBatch Create(
        Guid id,
        Guid campaignId,
        string fileName,
        string fileSha256,
        DateTimeOffset uploadedAtUtc,
        string uploadedBy,
        int rowsTotal,
        int rowsMatched,
        int rowsUnmatched,
        int rowsInvalid)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainRuleViolationException(DomainErrorCodes.ValidationFailed, "File name is required.");
        }

        if (fileSha256.Length != Sha256HexLength)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                $"File hash must be {Sha256HexLength} hexadecimal characters.");
        }

        if (rowsMatched + rowsUnmatched + rowsInvalid != rowsTotal)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.ValidationFailed,
                "Every row of the file has to end up in exactly one of the three outcomes.");
        }

        // A file is reported as completed with errors only because of rows that could not be read.
        // Unmatched rows are a normal, expected outcome and are reported, not treated as failures.
        var status = rowsInvalid > 0 ? ImportBatchStatus.CompletedWithErrors : ImportBatchStatus.Completed;

        return new ImportBatch(
            id,
            campaignId,
            fileName,
            fileSha256,
            uploadedAtUtc,
            uploadedBy,
            rowsTotal,
            rowsMatched,
            rowsUnmatched,
            rowsInvalid,
            status);
    }
}
