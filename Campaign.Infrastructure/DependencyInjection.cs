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

        AddCustomerDirectory(services, configuration);

        return services;
    }

    /// <summary>
    /// Directory:Provider decides which implementation of the port is registered. Everything above
    /// this line, and every use case, is written against ICustomerDirectory and cannot tell.
    /// </summary>
    private static void AddCustomerDirectory(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Directory:Provider"] ?? DefaultDirectoryProvider;

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ICustomerDirectory, InMemoryCustomerDirectory>();
            return;
        }

        if (!string.Equals(provider, "Soap", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Directory:Provider is '{provider}'; the supported values are 'Soap' and 'InMemory'.");
        }

        // A factory, not an instance: each retry needs its own channel, because a WCF channel that
        // has failed once cannot be used again.
        services.AddScoped<ICustomerDirectory>(_ => new SoapCustomerDirectory(CreateSoapClient));
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
