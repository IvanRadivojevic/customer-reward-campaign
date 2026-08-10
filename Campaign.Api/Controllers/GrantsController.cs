namespace Campaign.Api.Controllers;

using Campaign.Api.Auth;
using Campaign.Api.Contracts;
using Campaign.Api.Errors;
using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class GrantsController : ControllerBase
{
    /// <summary>Set on a replayed answer so the caller can tell it apart from a fresh grant.</summary>
    public const string ReplayedHeader = "Idempotency-Replayed";

    private readonly CreateGrant _createGrant;
    private readonly VoidGrant _voidGrant;
    private readonly ListGrants _listGrants;
    private readonly ICallerContext _caller;

    public GrantsController(
        CreateGrant createGrant,
        VoidGrant voidGrant,
        ListGrants listGrants,
        ICallerContext caller)
    {
        _createGrant = createGrant;
        _voidGrant = voidGrant;
        _listGrants = listGrants;
        _caller = caller;
    }

    /// <summary>Awards the campaign discount to one customer.</summary>
    /// <remarks>
    /// The Idempotency-Key header is required. Repeating a request with the same key returns the
    /// grant that was already made, with 200 and Idempotency-Replayed: true, instead of a second
    /// grant. The same key used for a different customer or campaign is refused.
    /// </remarks>
    [HttpPost("campaigns/{campaignId:guid}/grants")]
    [ProducesResponseType(typeof(GrantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GrantResponse>> Create(
        Guid campaignId,
        [FromBody] CreateGrantRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ApiErrorException(
                DomainErrorCodes.ValidationFailed,
                "The Idempotency-Key header is required.");
        }

        var result = await _createGrant.ExecuteAsync(
            new CreateGrantCommand(campaignId, request.CustomerExternalId, _caller.ExternalUserId, idempotencyKey),
            ct);

        var body = GrantResponse.From(result.Grant);

        if (result.Replayed)
        {
            Response.Headers[ReplayedHeader] = "true";
            return Ok(body);
        }

        return CreatedAtAction(nameof(GetById), new { grantId = body.Id }, body);
    }

    /// <summary>One grant by id.</summary>
    [HttpGet("grants/{grantId:guid}")]
    [ProducesResponseType(typeof(GrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GrantResponse>> GetById(Guid grantId, CancellationToken ct)
    {
        var grants = await _listGrants.ExecuteAsync(
            new ListGrantsQuery(_caller.ExternalUserId, _caller.IsAdmin),
            ct);

        var grant = grants.FirstOrDefault(candidate => candidate.Id == grantId)
            ?? throw new ApiErrorException(DomainErrorCodes.GrantNotFound, "Unknown grant.");

        return Ok(GrantResponse.From(grant));
    }

    /// <summary>Voids a grant. Nothing is deleted; the record keeps the reason, the actor and the time.</summary>
    [HttpPost("grants/{grantId:guid}/void")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void(
        Guid grantId,
        [FromBody] VoidGrantRequest? request,
        CancellationToken ct)
    {
        await _voidGrant.ExecuteAsync(
            new VoidGrantCommand(grantId, _caller.ExternalUserId, _caller.IsAdmin, request?.Reason),
            ct);

        return NoContent();
    }

    /// <summary>Lists grants. An agent sees their own; an admin sees everybody's.</summary>
    [HttpGet("grants")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<GrantResponse>>> List(
        [FromQuery] Guid? campaignId,
        [FromQuery] Guid? agentId,
        [FromQuery] DateOnly? businessDate,
        [FromQuery] GrantStatus? status,
        CancellationToken ct)
    {
        var grants = await _listGrants.ExecuteAsync(
            new ListGrantsQuery(
                _caller.ExternalUserId,
                _caller.IsAdmin,
                campaignId,
                agentId,
                businessDate,
                status),
            ct);

        return Ok(grants.Select(GrantResponse.From).ToList());
    }
}
