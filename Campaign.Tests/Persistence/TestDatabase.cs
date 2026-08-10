namespace Campaign.Tests.Persistence;

using Microsoft.Data.SqlClient;

/// <summary>
/// The database the tests are allowed to use. It is a separate catalogue on the same server, so a
/// local run never touches the one the API is using.
/// </summary>
internal static class TestDatabase
{
    private const string DatabaseName = "CampaignTests";

    public static string ConnectionString
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Campaign")
                ?? throw new InvalidOperationException(
                    "The database tests need the ConnectionStrings__Campaign environment variable. "
                    + "Locally: docker compose up -d, then use the connection string from appsettings.Example.json.");

            return new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = DatabaseName
            }.ConnectionString;
        }
    }
}
