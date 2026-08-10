namespace Campaign.Api.Middleware;

/// <summary>
/// Every response carries X-Correlation-Id: the one the caller sent, or a new one. The header is
/// attached when the response starts rather than up front, so it survives the exception handler
/// clearing the response on its way to a ProblemDetails body.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString() : incoming;

        // TraceIdentifier is what the logs and the ProblemDetails body already quote, so pointing it
        // at the correlation id keeps one identifier across the response, the body and the log.
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
