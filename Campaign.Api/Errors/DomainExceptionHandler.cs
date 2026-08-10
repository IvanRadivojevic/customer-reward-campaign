namespace Campaign.Api.Errors;

using System.Globalization;
using Campaign.Core.Domain;
using Campaign.Core.Ports;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Turns the exceptions the domain and the adapters raise into RFC 7807 responses. Because every
/// refusal carries a code from the catalogue, this is a single mapping rather than a chain of type
/// checks that has to grow with every new rule.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Describe(exception);
        if (problem is null)
        {
            return false;
        }

        _logger.LogInformation(
            "Request refused with {ErrorType}: {Detail}",
            problem.Type,
            problem.Detail);

        if (problem.Status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)ApiErrorCodes.DirectoryRetryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;
        problem.Instance = httpContext.Request.Path;

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: ProblemResponse.ProblemContentType,
            cancellationToken);

        return true;
    }

    private static ProblemDetails? Describe(Exception exception) => exception switch
    {
        DomainRuleViolationException domain => Build(domain.Code, domain.Message, domain.Details),
        ApiErrorException api => Build(api.Code, api.Message),
        DirectoryUnavailableException directory => Build(
            ApiErrorCodes.DirectoryUnavailable,
            "The customer catalogue could not be reached, so no grant was created.",
            new Dictionary<string, object?> { ["reason"] = directory.Message }),
        _ => null
    };

    private static ProblemDetails Build(
        string code,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Type = code,
            Title = code,
            Status = ApiErrorCodes.StatusFor(code),
            Detail = detail
        };

        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        return problem;
    }
}
