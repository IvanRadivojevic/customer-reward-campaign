namespace Campaign.Api.Controllers;

using System.Security.Cryptography;
using Campaign.Api.Auth;
using Campaign.Api.Contracts;
using Campaign.Api.RateLimiting;
using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class ImportsController : ControllerBase
{
    /// <summary>Ten megabytes, as agreed. A purchase report for one weekly campaign is far smaller.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = [".csv"];

    private static readonly string[] AllowedContentTypes =
    [
        "text/csv",
        "application/csv",
        "text/plain",
        "application/vnd.ms-excel",
        "application/octet-stream"
    ];

    private readonly ImportPurchases _import;
    private readonly GetImportBatch _getBatch;
    private readonly ICallerContext _caller;

    public ImportsController(ImportPurchases import, GetImportBatch getBatch, ICallerContext caller)
    {
        _import = import;
        _getBatch = getBatch;
        _caller = caller;
    }

    /// <summary>Uploads the purchase report of a campaign and matches every row against its grants.</summary>
    /// <remarks>
    /// The answer is 200 with the summary of the batch, never 202: the file is processed while the
    /// request is open. Sending the same file again returns the batch that already exists rather than
    /// importing it twice.
    /// </remarks>
    [HttpPost("campaigns/{campaignId:guid}/imports")]
    [Authorize(Policy = CampaignPolicies.CanImport)]
    [EnableRateLimiting(RateLimitingExtensions.ImportPolicy)]
    [ProducesResponseType(typeof(ImportBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ImportBatchResponse>> Upload(
        Guid campaignId,
        IFormFile file,
        CancellationToken ct)
    {
        Validate(file);

        // The file is read into memory and never written to disk, so the name it arrived with is
        // only ever data on a record - it can never become a path.
        using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;

        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;

        var result = await _import.ExecuteAsync(
            new ImportPurchasesCommand(
                campaignId,
                Path.GetFileName(file.FileName),
                sha256,
                _caller.ExternalUserId,
                content),
            ct);

        return Ok(ImportBatchResponse.From(result.Batch, result.AlreadyImported));
    }

    /// <summary>The status of one import, with every row it produced.</summary>
    [HttpGet("imports/{batchId:guid}")]
    [Authorize(Policy = CampaignPolicies.CanImport)]
    [ProducesResponseType(typeof(ImportBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImportBatchDetailResponse>> GetById(Guid batchId, CancellationToken ct)
    {
        var view = await _getBatch.ExecuteAsync(batchId, ct);

        return Ok(ImportBatchDetailResponse.From(view));
    }

    private static void Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw new DomainRuleViolationException(DomainErrorCodes.CsvInvalid, "No file was uploaded.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.CsvInvalid,
                $"The file is larger than the {MaxFileSizeBytes / (1024 * 1024)} MB this endpoint accepts.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.CsvInvalid,
                $"'{extension}' is not a CSV file.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                DomainErrorCodes.CsvInvalid,
                $"Content type '{file.ContentType}' is not accepted for a CSV upload.");
        }
    }
}
