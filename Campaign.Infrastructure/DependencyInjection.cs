namespace Campaign.Infrastructure;

using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Campaign.Infrastructure.Persistence;
using Campaign.Infrastructure.Soap;
using Campaign.Infrastructure.Soap.Generated;
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

    /// <summary>The SOAP catalogue is the real source; InMemory has to be asked for explicitly.</summary>
    private const string DefaultDirectoryProvider = "Soap";

    /// <summary>
    /// Every value is read when the service is resolved, not while it is being registered. That is
    /// deliberate: a host that layers configuration on after the registrations - which is exactly
    /// what the integration tests do - would otherwise be ignored, and the application would quietly
    /// run against the wrong database or the wrong catalogue.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((_, options) =>
            options.UseCampaignSqlServer(ConnectionString(configuration)));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new BusinessDateProvider(
            provider.GetRequiredService<TimeProvider>(),
            configuration["Campaign:TimeZoneId"] ?? DefaultTimeZoneId));

        services.AddScoped<IGrantRepository, EfGrantRepository>();
        services.AddScoped<IImportRepository, EfImportRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<DatabaseInitializer>();

        services.AddScoped(_ => CreateCustomerDirectory(configuration));

        return services;
    }

    private static string ConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("Campaign")
        ?? throw new InvalidOperationException(
            "Connection string 'Campaign' is not configured. See appsettings.Example.json.");

    /// <summary>
    /// Directory:Provider decides which implementation of the port answers. Every use case is written
    /// against ICustomerDirectory and cannot tell which one it got.
    /// </summary>
    private static ICustomerDirectory CreateCustomerDirectory(IConfiguration configuration)
    {
        var provider = configuration["Directory:Provider"] ?? DefaultDirectoryProvider;

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return new InMemoryCustomerDirectory();
        }

        if (!string.Equals(provider, "Soap", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Directory:Provider is '{provider}'; the supported values are 'Soap' and 'InMemory'.");
        }

        // A factory, not an instance: each retry needs its own channel, because a WCF channel that
        // has failed once cannot be used again.
        return new SoapCustomerDirectory(CreateSoapClient);
    }

    /// <summary>
    /// The endpoint address and the binding both come from the WSDL the client was generated from,
    /// so there is no second place where the service address is written down. The five second budget
    /// goes onto the channel as well, because a WCF call cannot be cancelled from outside once it is
    /// in flight.
    /// </summary>
    private static SOAPDemoSoap CreateSoapClient()
    {
        var client = new SOAPDemoSoapClient();
        client.Endpoint.Binding.OpenTimeout = SoapCustomerDirectory.AttemptTimeout;
        client.Endpoint.Binding.SendTimeout = SoapCustomerDirectory.AttemptTimeout;
        client.Endpoint.Binding.ReceiveTimeout = SoapCustomerDirectory.AttemptTimeout;
        return client;
    }
}
