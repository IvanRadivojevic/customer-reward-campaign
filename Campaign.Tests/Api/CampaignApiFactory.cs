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
    /// <summary>Long enough for HS256, and it never leaves the test run.</summary>
    public const string SigningKey = "integration-tests-signing-key-that-is-long-enough-for-hs256";

    public const string Issuer = "campaign-api";
    public const string Audience = "campaign-api";

    private readonly Action<IServiceCollection>? _overrideServices;
    private readonly string _environment;

    public CampaignApiFactory()
        : this(null, "Development")
    {
    }

    private CampaignApiFactory(Action<IServiceCollection>? overrideServices, string environment)
    {
        _overrideServices = overrideServices;
        _environment = environment;
    }

    /// <summary>The application as it runs outside Development, where the token endpoint is gone.</summary>
    public static CampaignApiFactory AsProduction() => new(null, "Production");

    /// <summary>A catalogue that is always down, for reaching directory-unavailable without a network.</summary>
    public static CampaignApiFactory WithBrokenDirectory() =>
        new(
            services =>
            {
                services.RemoveAll<ICustomerDirectory>();
                services.AddScoped<ICustomerDirectory, AlwaysFailingCustomerDirectory>();
            },
            "Development");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Campaign"] = TestDatabase.ConnectionString,
                ["Directory:Provider"] = "InMemory",
                ["Campaign:TimeZoneId"] = "Europe/Belgrade",
                ["Auth:SigningKey"] = SigningKey,
                ["Auth:Issuer"] = Issuer,
                ["Auth:Audience"] = Audience
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
