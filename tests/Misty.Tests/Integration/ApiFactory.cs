using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Misty.Infrastructure.Persistence;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Testcontainers.ServiceBus;

namespace Misty.Tests.Integration;

// Shared WebApplicationFactory used by all integration test collections. Containers start once per collection, not once per test.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Misty_Test_2024!")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:alpine")
        .Build();

    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .Build();

    private readonly ServiceBusContainer _serviceBus = new ServiceBusBuilder()
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-config.test.json"))
        .Build();

    public string SqlConnectionString => _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync(), _azurite.StartAsync(), _serviceBus.StartAsync());

        // Expose testcontainer connection strings as environment variables so they are picked up by WebApplication.CreateBuilder at the point Program.cs reads them (before DeferredHostBuilder.Build() applies ConfigureAppConfiguration callbacks).
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", _sql.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis",
            $"localhost:{_redis.GetMappedPublicPort(6379)}");
        Environment.SetEnvironmentVariable("ConnectionStrings__ServiceBus", _serviceBus.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__BlobStorage", _azurite.GetConnectionString());
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
        Environment.SetEnvironmentVariable("ConnectionStrings__BlobStorage", null);
        Environment.SetEnvironmentVariable("Jwt__Key", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _azurite.DisposeAsync().AsTask(), _serviceBus.DisposeAsync().AsTask());
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
            // Override BlobServiceClient singleton to point at the Azurite test container.
            // Also pin the service version to V2024_08_04 — Azurite 3.35.0 does not yet support the 2026-04-06 API used by Azure.Storage.Blobs 12.28.0 by default.
            var existingBlob = services.SingleOrDefault(d => d.ServiceType == typeof(BlobServiceClient));
            if (existingBlob is not null) services.Remove(existingBlob);
            var blobOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_08_04);
            services.AddSingleton(new BlobServiceClient(_azurite.GetConnectionString(), blobOptions));

            // The Service Bus emulator does not implement the HTTPS management API used by ServiceBusAdministrationClient health checks, so the health check is stubbed in tests.
            // Actual message processing still runs against the emulator through the AMQP endpoint.
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
