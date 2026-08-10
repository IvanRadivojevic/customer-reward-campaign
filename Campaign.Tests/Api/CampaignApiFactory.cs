namespace Campaign.Tests.Api;

using Campaign.Core.Ports;
using Campaign.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Hosts the real application in memory, against the test database and with the in-memory customer
/// catalogue, so an integration test exercises the whole pipeline without touching the network.
/// </summary>
public sealed class CampaignApiFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _overrideServices;

    public CampaignApiFactory()
        : this(null)
    {
    }

    private CampaignApiFactory(Action<IServiceCollection>? overrideServices)
    {
        _overrideServices = overrideServices;
    }

    /// <summary>A catalogue that is always down, for reaching directory-unavailable without a network.</summary>
    public static CampaignApiFactory WithBrokenDirectory() =>
        new(services =>
        {
            services.RemoveAll<ICustomerDirectory>();
            services.AddScoped<ICustomerDirectory, AlwaysFailingCustomerDirectory>();
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development is what registers the header based caller identity that stands in for
        // authentication until the next work package.
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Campaign"] = TestDatabase.ConnectionString,
                ["Directory:Provider"] = "InMemory",
                ["Campaign:TimeZoneId"] = "Europe/Belgrade"
            }));

        if (_overrideServices is not null)
        {
            builder.ConfigureTestServices(_overrideServices);
        }
    }
}

public sealed class AlwaysFailingCustomerDirectory : ICustomerDirectory
{
    public Task<CustomerDto?> FindByIdAsync(string externalCustomerId, CancellationToken ct) =>
        throw new DirectoryUnavailableException("The catalogue is down.");

    public Task<IReadOnlyList<CustomerDto>> SearchByNameAsync(string name, CancellationToken ct) =>
        throw new DirectoryUnavailableException("The catalogue is down.");
}
