# Customer Reward Campaign

A .NET 10 API for managing customer reward campaigns and measuring purchase conversion.
The solution integrates an external SOAP customer directory with CSV purchase reports.

## Projects

| Project | Contains |
|---|---|
| `Campaign.Core` | Entities, business rules, use cases and ports. No NuGet references. |
| `Campaign.Infrastructure` | EF Core persistence, SOAP customer directory, CSV reader. |
| `Campaign.Api` | Controllers, authentication, OpenAPI and the static agent page. |
| `Campaign.Tests` | xUnit tests. |

Dependencies point one way only: `Api -> Infrastructure -> Core` and `Api -> Core`. Because
`Campaign.Core` references nothing, the business rules can be tested without a database or a web host.

## Build and test

The .NET 10 SDK is required; `dotnet --list-sdks` has to list a 10.x entry.

```bash
dotnet build
dotnet test
```

Database setup, configuration and the demo walkthrough are documented as those parts are added.
