# SOAP fixtures

**Everything in this folder is synthetic.** These are not recorded responses of the live service.

The specification asks for two or three real responses recorded from
`https://www.crcind.com/csp/samples/SOAP.Demo.cls`. That service was unreachable while this package
was built - it refused connections on both 80 and 443 - so no real response could be captured, and
the Internet Archive holds no recorded response for `FindPerson` or `GetListByName` either.

The files here were therefore written by hand from the contract in
[`docs/soap/SOAP.Demo.wsdl`](../../../docs/soap/SOAP.Demo.wsdl): element names, namespaces, order
and types follow that WSDL exactly, and the values are made up. Every file says so in its first
comment and carries `synthetic-` in its name.

They are not decoration: `SoapCustomerDirectoryTests` parses them and feeds the result through the
adapter, so a fixture that stopped matching the generated contract would fail the build.

When the service comes back, replacing these with genuine recordings means saving the real
envelopes under `recorded-*.xml` and pointing the tests at them; nothing else has to change.
