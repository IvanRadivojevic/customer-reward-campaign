namespace Campaign.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Used only by the dotnet-ef tooling. A migration is generated from the model and never touches a
/// server, so the connection string only has to be well formed; it carries no credentials for that
/// reason. Having this factory means "migrations add" works on a clean checkout, without a running
/// database and without local configuration.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimePlaceholder =
        "Server=localhost,1433;Database=Campaign;Integrated Security=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Campaign") ?? DesignTimePlaceholder;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
