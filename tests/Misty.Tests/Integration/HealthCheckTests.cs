using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Misty.Tests.Integration;

[Collection("Integration")]
public sealed class HealthCheckTests
{
    private readonly HttpClient _client;

    public HealthCheckTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsHealthyWithAllChecks()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");

        var entries = root.GetProperty("entries");
        entries.GetProperty("sql").GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("redis").GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("service-bus").GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("blob-storage").GetProperty("status").GetString().Should().Be("Healthy");
    }
}
