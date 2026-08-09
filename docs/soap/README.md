# SOAP contract

`SOAP.Demo.wsdl` is the contract the customer directory client is generated from. Generation reads
this file and never the network, so the build works offline and does not depend on a public demo
service staying up.

## Where this copy came from

The live service at `https://www.crcind.com/csp/samples/SOAP.Demo.cls` was refusing connections on
both port 80 and port 443 while this package was built, so the file could not be fetched from it.

This copy is therefore the Internet Archive snapshot of
`https://www.crcind.com/csp/samples/SOAP.Demo.CLS?WSDL=1` taken on **14 October 2025**, 18,732 bytes:

```
https://web.archive.org/web/20251014133414id_/https://www.crcind.com/csp/samples/SOAP.Demo.CLS?WSDL=1
```

The Archive holds a snapshot of the same URL from June 2025 with an identical content digest, so the
contract had not changed for months before that capture.

Two independent checks say the copy is sound. The field names and types of `PersonIdentification`
(`ID`, `Name`, `SSN`, `DOB`) match what the service returned when it last answered. And the client
generated from this file compiles against the operations the solution uses.

## Regenerating the client

```bash
dotnet tool install --global dotnet-svcutil
dotnet-svcutil docs/soap/SOAP.Demo.wsdl --namespace "*,Campaign.Infrastructure.Soap.Generated" --outputFile SoapDemoClient.cs
```

Run this in an empty directory and copy the result to
`Campaign.Infrastructure/Soap/Generated/`. `dotnet-svcutil` 8.0.0 builds its helper project against
net8.0 and copies the target project's package references into it, so running it inside
`Campaign.Infrastructure` fails on EF Core 10, which does not support net8.0.

The WSDL imports two schemas, `SOAP.ByNameDataSet.cls?XSD` and `SOAP.Demo.QueryByName.DS.cls?XSD`,
that cannot be resolved offline. They only describe `GetDataSetByName` and `QueryByName`, which this
solution does not call, and generation succeeds without them.

## What is used

Of the nine operations, exactly two are called:

| Operation | Signature | Used for |
|---|---|---|
| `FindPerson` | `Task<Person> FindPersonAsync(string id)` | `ICustomerDirectory.FindByIdAsync` |
| `GetListByName` | `Task<PersonIdentification[]> GetListByNameAsync(string name)` | `ICustomerDirectory.SearchByNameAsync` |

`Person` carries no id of its own, so a customer found by id keeps the id it was asked for. Both
types also carry `SSN` and `DOB`; `CustomerDto` takes only the id and the name, so neither a social
security number nor a date of birth ever enters this system.
