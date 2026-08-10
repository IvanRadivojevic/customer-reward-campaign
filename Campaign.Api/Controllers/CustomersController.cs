namespace Campaign.Api.Controllers;

using Campaign.Api.Auth;
using Campaign.Api.Contracts;
using Campaign.Api.Errors;
using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Reads the external customer catalogue through the port. Which implementation answers - the SOAP
/// service or the in-memory one - is a matter of configuration, not of this controller.
/// </summary>
[ApiController]
[Authorize(Policy = CampaignPolicies.CanReadCustomers)]
[Route("api/v1/customers")]
[Produces("application/json")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerDirectory _customers;

    public CustomersController(ICustomerDirectory customers)
    {
        _customers = customers;
    }

    /// <summary>Searches the catalogue by name. Example: ?name=Ana</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> Search(
        [FromQuery] string? name,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiErrorException(DomainErrorCodes.ValidationFailed, "A name to search for is required.");
        }

        var matches = await _customers.SearchByNameAsync(name, ct);

        return Ok(matches.Select(CustomerResponse.From).ToList());
    }

    /// <summary>One customer by their catalogue id. Example: /api/v1/customers/1</summary>
    [HttpGet("{externalId}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CustomerResponse>> GetById(string externalId, CancellationToken ct)
    {
        var customer = await _customers.FindByIdAsync(externalId, ct)
            ?? throw new ApiErrorException(
                DomainErrorCodes.CustomerNotFound,
                "The customer catalogue does not know this customer.");

        return Ok(CustomerResponse.From(customer));
    }
}
