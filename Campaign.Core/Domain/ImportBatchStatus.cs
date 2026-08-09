namespace Campaign.Core.Domain;

/// <summary>
/// Only two states, because the import is synchronous: nobody ever observes a batch while it is
/// being processed, and a file that cannot be read at all is rejected before a batch is created.
/// </summary>
public enum ImportBatchStatus
{
    Completed = 0,
    CompletedWithErrors = 1
}
