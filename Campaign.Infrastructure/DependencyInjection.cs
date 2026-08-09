namespace Campaign.Infrastructure;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Campaign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The single seam between the web layer and this project: Campaign.Api calls AddInfrastructure and
/// knows nothing else about what is behind the ports.
/// </summary>
public static class DependencyInjection
{
    private const string DefaultTimeZoneId = "Europe/Belgrade";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Campaign")
            ?? throw new InvalidOperationException(
                "Connection string 'Campaign' is not configured. See appsettings.Example.json.");

        services.AddDbContext<AppDbContext>(options => options.UseCampaignSqlServer(connectionString));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new BusinessDateProvider(
            provider.GetRequiredService<TimeProvider>(),
            configuration["Campaign:TimeZoneId"] ?? DefaultTimeZoneId));

        services.AddScoped<IGrantRepository, EfGrantRepository>();
        services.AddScoped<IImportRepository, EfImportRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
