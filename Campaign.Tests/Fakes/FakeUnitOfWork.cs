namespace Campaign.Tests.Fakes;

using Campaign.Core.Ports;

/// <summary>
/// Runs the operation and, if it fails, puts the grants back the way they were - the one property of
/// a transaction the use case tests actually depend on.
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly FakeGrantRepository _grants;

    public FakeUnitOfWork(FakeGrantRepository grants)
    {
        _grants = grants;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var snapshot = _grants.Grants.ToList();

        try
        {
            return await operation(ct);
        }
        catch
        {
            _grants.Grants.Clear();
            _grants.Grants.AddRange(snapshot);
            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
