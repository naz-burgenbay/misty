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
public sealed class ChannelRoleTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ChannelRoleTests(ApiFactory factory)
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

    private async Task<string> RegisterAndLoginAsync(string username)
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });
        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> CreateChannelAsync(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "role-test",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    [Fact]
    public async Task GetRoles_AfterChannelCreation_ReturnsOwnerRole()
    {
        var token = await RegisterAndLoginAsync("role_owner1");
        var channelId = await CreateChannelAsync(token);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}/roles");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await resp.Content.ReadFromJsonAsync<JsonElement>();
        roles.GetArrayLength().Should().Be(1);
        roles[0].GetProperty("name").GetString().Should().Be("Owner");
        roles[0].GetProperty("isOwnerRole").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CreateRole_ValidInput_Returns201()
    {
        var token = await RegisterAndLoginAsync("role_owner2");
        var channelId = await CreateChannelAsync(token);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Moderator",
            Permissions = 7L,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Moderator");
        body.GetProperty("isOwnerRole").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRole_NonOwnerRole_Returns200()
    {
        var token = await RegisterAndLoginAsync("role_owner3");
        var channelId = await CreateChannelAsync(token);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Mod",
            Permissions = 3L,
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = created.GetProperty("roleId").GetGuid();

        string version;
        await using (var db0 = _factory.CreateDbContext())
            version = Convert.ToBase64String((await db0.ChannelRoles.IgnoreQueryFilters().FirstAsync(r => r.Id == roleId)).Version);

        var updateResp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}/roles/{roleId}", new
        {
            Name = "Moderator Updated",
            Permissions = 7L,
            Version = version,
        });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Moderator Updated");
    }

    [Fact]
    public async Task UpdateRole_OwnerRole_Returns409()
    {
        var token = await RegisterAndLoginAsync("role_owner4");
        var channelId = await CreateChannelAsync(token);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rolesResp = await _client.GetAsync($"/api/v1/channels/{channelId}/roles");
        var roles = await rolesResp.Content.ReadFromJsonAsync<JsonElement>();
        var ownerRoleId = roles[0].GetProperty("roleId").GetGuid();

        var resp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}/roles/{ownerRoleId}", new
        {
            Name = "Renamed Owner",
            Permissions = 0L,
            Version = "AAAAAAAAAAA=",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteRole_NonOwnerRole_Returns204()
    {
        var token = await RegisterAndLoginAsync("role_owner5");
        var channelId = await CreateChannelAsync(token);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Temp",
            Permissions = 1L,
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = created.GetProperty("roleId").GetGuid();

        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/roles/{roleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteRole_OwnerRole_Returns409()
    {
        var token = await RegisterAndLoginAsync("role_owner6");
        var channelId = await CreateChannelAsync(token);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rolesResp = await _client.GetAsync($"/api/v1/channels/{channelId}/roles");
        var roles = await rolesResp.Content.ReadFromJsonAsync<JsonElement>();
        var ownerRoleId = roles[0].GetProperty("roleId").GetGuid();

        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/roles/{ownerRoleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
