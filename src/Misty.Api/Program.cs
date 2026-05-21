using Microsoft.EntityFrameworkCore;
using Misty.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Misty.Api...");
    await Run(args);
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Misty.Api terminated unexpectedly.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task Run(string[] args)
{

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// AddDbContextFactory is registered so that background services like outbox relay or cache invalidation worker can create their own DbContext instances without depending on the scoped lifetime of the main ApplicationDbContext
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus")
    ?? throw new InvalidOperationException("Connection string 'ServiceBus' is not configured.");

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("sql")
    .AddRedis(redisConnectionString, "redis")
    .AddAzureServiceBusTopic(serviceBusConnectionString, "message-events", name: "service-bus");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");

app.Run();
}

// Expose Program to the test project for WebApplicationFactory<Program>
public partial class Program { }

