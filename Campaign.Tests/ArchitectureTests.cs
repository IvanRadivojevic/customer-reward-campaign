using System.Reflection;

namespace Campaign.Tests;

/// <summary>
/// Guards the agreed dependency direction (Api -> Infrastructure -> Core, Api -> Core).
/// Campaign.Core is a plain class library and must not see persistence, web or SOAP types.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] AssembliesForbiddenInCore =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System.ServiceModel",
        "Campaign.Infrastructure",
        "Campaign.Api"
    ];

    [Fact]
    public void Core_does_not_reference_infrastructure_or_web_assemblies()
    {
        var core = Assembly.Load("Campaign.Core");

        var violations = core.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => AssembliesForbiddenInCore.Any(forbidden => name.StartsWith(forbidden, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Campaign.Core must stay free of infrastructure, but references: {string.Join(", ", violations)}.");
    }
}
