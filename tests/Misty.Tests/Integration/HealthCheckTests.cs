using System.Net;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Misty.Tests.Integration;

// Integration test that verifies the /health endpoint returns 200 Healthy.
// SQL and Redis are started via Testcontainers for full isolation. Service Bus is intentionally stubbed out with an always-healthy check because the emulator requires its own dedicated SQL Server instance which is too resource-heavy to run alongside Testcontainers SQL.
// SB connectivity is validated by the docker-compose stack (run `docker compose up -d` and hit /health manually) and will be covered by Phase 4 pipeline integration tests that run against the full emulator environment.
public sealed class HealthCheckTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql;
    private readonly RedisContainer _redis;

    private const string SaPassword = "Misty_Test_2024!";

    public HealthCheckTests()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword(SaPassword)
            .Build();

        _redis = new RedisBuilder("redis:alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_redis.DisposeAsync().AsTask(), _sql.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Database"] = _sql.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = $"localhost:{_redis.GetMappedPublicPort(6379)}",
                        // ServiceBus connection string must still be present so Program.cs doesn't throw on startup. The actual check is replaced below with a stub.
                        ["ConnectionStrings:ServiceBus"] =
                            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;" +
                            "SharedAccessKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFreNZ2He5uvRZ1x1Hy5oqsqm0NYTJ/tAAAAAA==;" +
                            "UseDevelopmentEmulator=true;",
                    });
                });

                host.ConfigureServices(services =>
                {
                    var sbDescriptor = services.FirstOrDefault(d =>
                        d.ServiceType == typeof(HealthCheckRegistration) &&
                        d.ImplementationInstance is HealthCheckRegistration r &&
                        r.Name == "service-bus");

                    if (sbDescriptor is not null)
                        services.Remove(sbDescriptor);

                    services.AddHealthChecks()
                        .Add(new HealthCheckRegistration(
                            "service-bus",
                            _ => new StubHealthCheck(),
                            failureStatus: HealthStatus.Unhealthy,
                            tags: null));
                });
            });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

file sealed class StubHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => Task.FromResult(HealthCheckResult.Healthy("stubbed for Testcontainers runs"));
}
