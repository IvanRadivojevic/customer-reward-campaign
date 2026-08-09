namespace Campaign.Tests.Persistence;

using Campaign.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// From this work package on, the tests need a real SQL Server: what is being tested are indexes and
/// check constraints, and no in-memory provider enforces those. The connection string comes from the
/// environment so the same tests run locally against the docker container and on the build server.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "CampaignTests";

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Campaign")
            ?? throw new InvalidOperationException(
                "The database tests need the ConnectionStrings__Campaign environment variable. "
                + "Locally: docker compose up -d, then use the connection string from appsettings.Example.json.");

        // The tests get their own database on the same server, so running them never touches the
        // database the API is using.
        ConnectionString = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = TestDatabaseName
        }.ConnectionString;

        // This call is also the acceptance criterion "the migration applies to an empty database":
        // the very first run creates the database from nothing but the migration.
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseCampaignSqlServer(ConnectionString);
        return new AppDbContext(options.Options);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(nameof(SqlServerCollection))]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
