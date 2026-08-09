namespace Campaign.Tests.Soap;

using System.Globalization;
using System.Xml.Linq;
using Campaign.Infrastructure.Soap.Generated;

/// <summary>
/// Reads the synthetic envelopes in tests/fixtures/soap into the types the generated client returns.
/// Going through the files rather than building the objects in code is the point: if a fixture ever
/// stops matching the contract in the WSDL, these tests notice.
/// </summary>
internal static class SoapFixtures
{
    private static readonly XNamespace Tempuri = "http://tempuri.org";

    public static Person LoadPerson(string fileName)
    {
        var result = Load(fileName).Descendants(Tempuri + "FindPersonResult").Single();

        return new Person
        {
            Name = Value(result, "Name") ?? string.Empty,
            SSN = Value(result, "SSN") ?? string.Empty,
            DOB = Date(result, "DOB") ?? default,
            DOBSpecified = Date(result, "DOB") is not null
        };
    }

    public static PersonIdentification[] LoadPersonIdentifications(string fileName) =>
        Load(fileName)
            .Descendants(Tempuri + "PersonIdentification")
            .Select(row => new PersonIdentification
            {
                ID = Value(row, "ID") ?? string.Empty,
                Name = Value(row, "Name") ?? string.Empty,
                SSN = Value(row, "SSN") ?? string.Empty,
                DOB = Date(row, "DOB") ?? default,
                DOBSpecified = Date(row, "DOB") is not null
            })
            .ToArray();

    private static string? Value(XElement parent, string name) => parent.Element(Tempuri + name)?.Value;

    private static DateTime? Date(XElement parent, string name)
    {
        var raw = Value(parent, name);

        return string.IsNullOrWhiteSpace(raw)
            ? null
            : DateTime.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static XDocument Load(string fileName) =>
        XDocument.Load(Path.Combine(FixtureDirectory(), fileName));

    private static string FixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "soap");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"tests/fixtures/soap was not found in any directory above {AppContext.BaseDirectory}.");
    }
}
