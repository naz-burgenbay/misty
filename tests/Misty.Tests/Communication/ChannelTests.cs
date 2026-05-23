using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class ChannelTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ChannelTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = _factory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            SchemasToInclude = ["users", "comm"],
        });
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> LoginAsync(string username)
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = "Str0ngPass!",
        });

        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task CreateChannel_Public_Returns201WithChannelData()
    {
        var token = await LoginAsync("ch_create1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "general",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L, // ViewChannel | ReadHistory | SendMessages
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("channelId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("name").GetString().Should().Be("general");
        body.GetProperty("isPrivate").GetBoolean().Should().BeFalse();
        body.TryGetProperty("inviteCode", out var ic).Should().BeTrue();
        ic.ValueKind.Should().Be(JsonValueKind.Null); // public channels have no invite code
        body.GetProperty("defaultPermissions").GetInt64().Should().Be(7L);
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateChannel_Private_ReturnsInviteCode()
    {
        var token = await LoginAsync("ch_create2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "secret",
            IsPrivate = true,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isPrivate").GetBoolean().Should().BeTrue();
        var inviteCode = body.GetProperty("inviteCode").GetString();
        inviteCode.Should().NotBeNullOrEmpty();
        inviteCode!.Length.Should().Be(12);
    }

    [Fact]
    public async Task CreateChannel_WithEmptyName_Returns422()
    {
        var token = await LoginAsync("ch_create3");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetChannel_ExistingId_Returns200()
    {
        var token = await LoginAsync("ch_get1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "gettest",
            IsPrivate = false,
            IsAiAssistantEnabled = true,
            DefaultPermissions = 3L,
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = created.GetProperty("channelId").GetGuid();

        var getResp = await _client.GetAsync($"/api/v1/channels/{channelId}");

        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("channelId").GetGuid().Should().Be(channelId);
        body.GetProperty("name").GetString().Should().Be("gettest");
        body.GetProperty("isAiAssistantEnabled").GetBoolean().Should().BeTrue();
        body.GetProperty("memberCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetChannel_UnknownId_Returns404()
    {
        var token = await LoginAsync("ch_get2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync($"/api/v1/channels/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateChannel_WithValidVersion_UpdatesFields()
    {
        var token = await LoginAsync("ch_upd1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "before",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = created.GetProperty("channelId").GetGuid();
        var version = created.GetProperty("version").GetString()!;

        var putResp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}", new
        {
            Name = "after",
            IsAiAssistantEnabled = true,
            DefaultPermissions = 7L,
            Version = version,
        });

        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await putResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("after");
        body.GetProperty("isAiAssistantEnabled").GetBoolean().Should().BeTrue();
        body.GetProperty("defaultPermissions").GetInt64().Should().Be(7L);
        body.GetProperty("version").GetString().Should().NotBe(version, "rowversion must advance after update");
    }

    [Fact]
    public async Task UpdateChannel_WithStaleVersion_Returns409()
    {
        var token = await LoginAsync("ch_upd2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "concurrency-test",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = created.GetProperty("channelId").GetGuid();
        var originalVersion = created.GetProperty("version").GetString()!;

        // First update advances the version
        await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}", new
        {
            Name = "first-update",
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
            Version = originalVersion,
        });

        // Second update using the original (now stale) version must be rejected
        var conflictResp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}", new
        {
            Name = "second-update",
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
            Version = originalVersion,
        });

        conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateChannel_UnknownId_Returns404()
    {
        var token = await LoginAsync("ch_upd3");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.PutAsJsonAsync($"/api/v1/channels/{Guid.NewGuid()}", new
        {
            Name = "ghost",
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
            Version = Convert.ToBase64String(new byte[8]),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteChannel_ExistingId_Returns204()
    {
        var token = await LoginAsync("ch_del1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "to-delete",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = created.GetProperty("channelId").GetGuid();

        var deleteResp = await _client.DeleteAsync($"/api/v1/channels/{channelId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await _client.GetAsync($"/api/v1/channels/{channelId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteChannel_UnknownId_Returns404()
    {
        var token = await LoginAsync("ch_del2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.DeleteAsync($"/api/v1/channels/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
