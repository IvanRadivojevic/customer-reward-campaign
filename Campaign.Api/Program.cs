using System.Reflection;
using Campaign.Api.Auth;
using Campaign.Api.Errors;
using Campaign.Api.Middleware;
using Campaign.Core.Domain;
using Campaign.Core.UseCases;
using Campaign.Infrastructure;
using Campaign.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);

// The use cases are plain classes from Campaign.Core; they are registered here because Core carries
// no dependency injection package of its own.
builder.Services.AddScoped<CreateGrant>();
builder.Services.AddScoped<VoidGrant>();
builder.Services.AddScoped<GetQuota>();
builder.Services.AddScoped<ListGrants>();

// Until JWT bearer arrives, Development identifies the caller with request headers and every other
// environment answers 401. See Campaign.Api/Auth.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ICallerContext, DevelopmentHeaderCallerContext>();
}
else
{
    builder.Services.AddScoped<ICallerContext, UnauthenticatedCallerContext>();
}

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
    options.SwaggerDoc("v1", new() { Title = "Customer Reward Campaign", Version = "v1" });

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

app.UseHttpsRedirection();

app.MapControllers();

await app.RunAsync();

/// <summary>Exposed so the integration tests can host the application in memory.</summary>
public partial class Program;
