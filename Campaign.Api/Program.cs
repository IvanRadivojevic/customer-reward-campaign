using Campaign.Infrastructure;
using Campaign.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// The use cases from Campaign.Core are registered together with the controllers that reach them and
// the customer directory they depend on, not here: registering them before ICustomerDirectory has an
// implementation would only stop the application from starting.
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Migrations are applied on start; the seed is written only when the database is still empty.
await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitialiseAsync(CancellationToken.None);
}

app.UseHttpsRedirection();

app.MapControllers();

await app.RunAsync();
