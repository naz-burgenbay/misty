using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Misty.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Misty.Tests.Integration;

// Shared WebApplicationFactory used by all integration test collections. Containers start once per collection, not once per test.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Misty_Test_2024!")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:alpine")
        .Build();

    public string SqlConnectionString => _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());

        // Expose testcontainer connection strings as environment variables so they are picked up by WebApplication.CreateBuilder at the point Program.cs reads them (before DeferredHostBuilder.Build() applies ConfigureAppConfiguration callbacks).
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", _sql.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis",
            $"localhost:{_redis.GetMappedPublicPort(6379)}");
        Environment.SetEnvironmentVariable("ConnectionStrings__ServiceBus",
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;"
            + "SharedAccessKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFreNZ2He5uvRZ1x1Hy5oqsqm0NYTJ/tAAAAAA==;"
            + "UseDevelopmentEmulator=true;");
        Environment.SetEnvironmentVariable("Jwt__Key", "misty-super-secret-test-signing-key-2024!");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "Misty.Api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "Misty.Web");

        // Apply migrations now so Respawn can find the schema/tables before any test runs. The server's own MigrateAsync() call on startup will then be a no-op.
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__ServiceBus", null);
        Environment.SetEnvironmentVariable("Jwt__Key", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        return new ApplicationDbContext(options);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace the real service-bus health check with a stub.
            // Health check registrations live inside IOptions<HealthCheckOptions>, not as individual ServiceDescriptors, so options must be configured directly.
            services.PostConfigure<HealthCheckServiceOptions>(opts =>
            {
                var existing = opts.Registrations.FirstOrDefault(r => r.Name == "service-bus");
                if (existing is not null)
                    opts.Registrations.Remove(existing);

                opts.Registrations.Add(new HealthCheckRegistration(
                    "service-bus",
                    _ => new StubHealthCheck(),
                    failureStatus: HealthStatus.Unhealthy,
                    tags: null));
            });
        });
    }
}

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<ApiFactory> { }

file sealed class StubHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}
