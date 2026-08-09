namespace Campaign.Infrastructure.Persistence;

using System.Data;
using Campaign.Core.Ports;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The transaction boundary for the daily limit. The rule - count the active grants, compare with
/// the limit, insert - stays in the use case; what lives here is only the isolation level and the
/// fact that a deadlock repeats the whole thing.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public EfUnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        // Retries are configured on the connection, and EF Core refuses a hand-started transaction
        // unless the whole block runs inside the execution strategy. The strategy re-runs this
        // delegate, so a retry repeats the COUNT as well as the INSERT - which is the point: the
        // transaction was rolled back and somebody else's grant may have landed in the meantime.
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            async token =>
            {
                // A retry starts from scratch, so whatever the failed attempt left in the change
                // tracker has to go, or it would be inserted a second time alongside the new one.
                _db.ChangeTracker.Clear();

                await using var transaction =
                    await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

                var result = await operation(token);

                await transaction.CommitAsync(token);
                return result;
            },
            ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
