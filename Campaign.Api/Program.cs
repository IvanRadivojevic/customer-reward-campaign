using System.Reflection;
using Campaign.Api.Auth;
using Campaign.Api.Errors;
using Campaign.Api.Middleware;
using Campaign.Api.RateLimiting;
using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Campaign.Infrastructure;
using Campaign.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    // The development login is not merely refused outside Development - its routes are not there.
    if (!builder.Environment.IsDevelopment())
    {
        options.Conventions.Add(new RemoveDevelopmentOnlyControllers());
    }
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);

// The use cases are plain classes from Campaign.Core; they are registered here because Core carries
// no dependency injection package of its own.
builder.Services.AddScoped<CreateGrant>();
builder.Services.AddScoped<VoidGrant>();
builder.Services.AddScoped<GetQuota>();
builder.Services.AddScoped<ListGrants>();
builder.Services.AddScoped<ImportPurchases>();
builder.Services.AddScoped<GetImportBatch>();

builder.Services.AddScoped<ICallerContext, ClaimsCallerContext>();
builder.Services.AddScoped<DevelopmentTokenIssuer>();

builder.Services.AddCampaignAuthentication(builder.Configuration);
builder.Services.AddCampaignAuthorization();
builder.Services.AddCampaignRateLimiting();

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

// A model binding failure has to answer in the same shape as every other refusal, otherwise the
// error catalogue would have a hole exactly where a caller is most likely to land.
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ProblemDetails
        {
            Type = DomainErrorCodes.ValidationFailed,
            Title = DomainErrorCodes.ValidationFailed,
            Status = StatusCodes.Status400BadRequest,
            Detail = "The request body or its parameters could not be read.",
            Instance = context.HttpContext.Request.Path
        };

        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Customer Reward Campaign", Version = "v1" });

    options.AddSecurityDefinition(
        "bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token from POST /api/v1/auth/token."
        });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });

    var documentation = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(documentation))
    {
        options.IncludeXmlComments(documentation);
    }
});

var app = builder.Build();

// Migrations are applied on start; the seed is written only when the database is still empty.
await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitialiseAsync(CancellationToken.None);
}

// First in the pipeline, so the identifier exists for error responses too.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Authentication first, so the rate limiter can count per token rather than per connection.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

/// <summary>Exposed so the integration tests can host the application in memory.</summary>
public partial class Program;
