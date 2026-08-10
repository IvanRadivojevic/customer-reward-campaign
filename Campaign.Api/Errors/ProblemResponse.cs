namespace Campaign.Api.Errors;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Writes a refusal in the one shape this API uses. Authentication and authorisation answer outside
/// the exception pipeline, so they come here instead of through the exception handler.
/// </summary>
public static class ProblemResponse
{
    public const string ProblemContentType = "application/problem+json";

    public static async Task WriteAsync(HttpContext context, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Type = code,
            Title = code,
            Status = ApiErrorCodes.StatusFor(code),
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["correlationId"] = context.TraceIdentifier;

        context.Response.StatusCode = problem.Status!.Value;

        // The content type goes through WriteAsJsonAsync: setting Response.ContentType first does not
        // survive, because the json writer overwrites it with application/json.
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemContentType);
    }
}
