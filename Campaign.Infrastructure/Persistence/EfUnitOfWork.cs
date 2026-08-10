namespace Campaign.Infrastructure.Persistence;

using System.Data;
using Campaign.Core.Ports;
using Campaign.Infrastructure.Persistence.Configurations;
using Microsoft.Data.SqlClient;
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

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (RefusedAsDuplicate(exception) is { } reason)
        {
            throw new DuplicateGrantException(reason, exception);
        }
    }

    /// <summary>
    /// Translates the store's verdict into domain terms. The index names live in the entity
    /// configuration, so this is the only place that has to know SQL Server reports a duplicate key
    /// as error 2601 or 2627 and names the index in the message.
    /// </summary>
    private static DuplicateGrantReason? RefusedAsDuplicate(DbUpdateException exception)
    {
        const int duplicateKeyInIndex = 2601;
        const int duplicateKeyInConstraint = 2627;

        if (exception.InnerException is not SqlException sql
            || (sql.Number != duplicateKeyInIndex && sql.Number != duplicateKeyInConstraint))
        {
            return null;
        }

        if (sql.Message.Contains(RewardGrantConfiguration.ActiveCustomerIndexName, StringComparison.Ordinal))
        {
            return DuplicateGrantReason.CustomerAlreadyRewarded;
        }

        if (sql.Message.Contains(RewardGrantConfiguration.IdempotencyKeyIndexName, StringComparison.Ordinal))
        {
            return DuplicateGrantReason.IdempotencyKeyAlreadyUsed;
        }

        return null;
    }
}
