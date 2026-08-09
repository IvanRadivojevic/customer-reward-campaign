namespace Campaign.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// One place that decides how this solution talks to SQL Server, so the application and the tests
/// cannot end up with different retry behaviour behind the same code.
/// </summary>
public static class SqlServerOptionsExtensions
{
    /// <summary>SQL Server's error number for the session chosen as the deadlock victim.</summary>
    private const int DeadlockErrorNumber = 1205;

    public static void UseCampaignSqlServer(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(
            connectionString,
            sqlServer => sqlServer.EnableRetryOnFailure(
                // P-02: exactly one more attempt after a deadlock, then the error stands. Declaring
                // the deadlock transient makes the execution strategy the retry, instead of a hand
                // written loop competing with it.
                maxRetryCount: 1,
                maxRetryDelay: TimeSpan.FromMilliseconds(200),
                errorNumbersToAdd: [DeadlockErrorNumber]));
}
