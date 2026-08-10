namespace Campaign.Core.Ports;

/// <summary>
/// The store refused the batch because the same file has already been imported into this campaign.
/// P-08 is settled by attempting the insert and reading the winner back, never by asking first: two
/// uploads that arrive together would both find nothing and both write.
/// </summary>
public sealed class DuplicateImportBatchException : Exception
{
    public DuplicateImportBatchException(Exception innerException)
        : base("This file has already been imported into this campaign.", innerException)
    {
    }
}
