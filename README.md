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

## Security

Every endpoint requires a JWT bearer token except two: `/health`, and the development login at
`POST /api/v1/auth/token`, which cannot require one because it is what hands tokens out. The role in
the token decides what the rest may reach. In Development that login exchanges one of the seed
accounts for a token, and the signing key comes from User Secrets:

```bash
dotnet user-secrets set "Auth:SigningKey" "<a long random development-only string>" --project Campaign.Api
```

| Account | Password | Role |
|---|---|---|
| `agent-1`, `agent-2`, `agent-3` | `<account>-password` | `agent` |
| `admin-1` | `admin-1-password` | `admin` |
| `integration-1` | `integration-1-password` | `integration` |

These passwords unlock nothing but a local demo: the token endpoint is removed from the application
outside Development, so those routes do not exist there. Moving to Microsoft Entra ID means setting
`Auth:Authority` and `Auth:Audience` in configuration instead of a signing key - the handler then
fetches the issuer's keys itself, and no code changes.

Requests are limited to 100 per minute per token. There is no CORS: the API serves its own page from
the same origin.

## Customer directory

Customer data comes from an external SOAP service through the `ICustomerDirectory` port. Two
implementations exist and `Directory:Provider` picks one: `Soap` calls the real service, `InMemory`
keeps the demo working when that service is down. A `DataverseCustomerDirectory` implementing the
same port is how this solution would read customers from Dynamics 365, without a line changing in
`Campaign.Core`.

## Database

Docker runs SQL Server only; the API is started with `dotnet run` so it can be debugged normally.

```bash
cp .env.example .env
docker compose up -d
```

No credential is committed. `.env` holds the password of the local container, and the API reads its
connection string from User Secrets or the environment - `appsettings.Example.json` documents every
key the application reads.

```bash
dotnet user-secrets set "ConnectionStrings:Campaign" "Server=localhost,1433;Database=Campaign;User Id=sa;Password=<your .env password>;TrustServerCertificate=True;Encrypt=False" --project Campaign.Api
```

Migrations are applied when the API starts, and the seed - one campaign running from three days ago
to three days ahead, plus three agents - is written only when the database is still empty.

## Build and test

The .NET 10 SDK is required; `dotnet --list-sdks` has to list a 10.x entry.

```bash
dotnet build
dotnet test
```

The tests use their own `CampaignTests` database on the same server and never touch the one the API
uses. They read the server from the `ConnectionStrings__Campaign` environment variable:

```bash
ConnectionStrings__Campaign="Server=localhost,1433;Database=Campaign;User Id=sa;Password=<your .env password>;TrustServerCertificate=True;Encrypt=False" dotnet test
```

The demo walkthrough is documented as those parts are added.
