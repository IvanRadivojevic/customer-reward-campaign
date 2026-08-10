namespace Campaign.Api.RateLimiting;

using System.Globalization;
using System.Threading.RateLimiting;
using Campaign.Api.Auth;
using Campaign.Api.Errors;
using Microsoft.AspNetCore.RateLimiting;

public static class RateLimitingExtensions
{
    /// <summary>The named limiter the import endpoint carries. Applied when that endpoint arrives.</summary>
    public const string ImportPolicy = "import";

    private const int RequestsPerMinute = 100;
    private const int ImportsPerMinute = 10;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddCampaignRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Per token, not per connection: one busy agent must not spend the budget of the next.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = RequestsPerMinute,
                        Window = Window
                    }));

            options.AddPolicy(ImportPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = ImportsPerMinute,
                        Window = Window
                    }));

            // A refusal here answers in the same shape as every other refusal in the catalogue: a
            // caller parses one error format, not one for the rules and another for the limiter.
            options.OnRejected = async (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
                    ? window
                    : Window;

                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

                await ProblemResponse.WriteAsync(
                    context.HttpContext,
                    ApiErrorCodes.RateLimitExceeded,
                    "Too many requests. Wait for the window to end and try again.");
            };
        });

        return services;
    }

    /// <summary>
    /// The subject of the token when there is one. A request without a token has not been through
    /// authentication yet at this point in the pipeline, so it is counted per connection instead.
    /// </summary>
    private static string PartitionKey(HttpContext context)
    {
        var subject = context.User.FindFirst(ClaimsCallerContext.SubjectClaim)?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? $"ip:{context.Connection.RemoteIpAddress}"
            : $"sub:{subject}";
    }
}
