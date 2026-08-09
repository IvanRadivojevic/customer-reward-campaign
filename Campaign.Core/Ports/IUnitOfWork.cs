namespace Campaign.Core.Ports;

/// <summary>
/// The transaction boundary, described by what the domain needs rather than by how it is achieved.
/// The business rule - count the active grants, compare with the limit, then insert - lives in the
/// use case; the isolation level and the single retry on a deadlock are the implementation's job.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs the operation as one atomic unit, isolated strongly enough that a concurrent request
    /// cannot slip a grant in between the count and the insert (P-02).
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
