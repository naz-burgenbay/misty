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
public sealed class RoleAssignmentTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public RoleAssignmentTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
    {
        var regResp = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });
        var loginBody = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return (loginBody.GetProperty("accessToken").GetString()!, userId);
    }

    private async Task<Guid> CreateChannelAndJoinAsync(string ownerToken, string memberToken, Guid memberUserId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "assign-test",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = body.GetProperty("channelId").GetGuid();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });

        return channelId;
    }

    private async Task<Guid> CreateRoleAsync(string token, Guid channelId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Member",
            Permissions = 7L,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roleId").GetGuid();
    }

    [Fact]
    public async Task AssignRole_ToMember_Returns204()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("ra_owner1");
        var (memberToken, memberId) = await RegisterAndLoginAsync("ra_member1");
        var channelId = await CreateChannelAndJoinAsync(ownerToken, memberToken, memberId);
        var roleId = await CreateRoleAsync(ownerToken, channelId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AssignRole_ToNonMember_Returns404()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("ra_owner2");
        var (nonMemberToken, nonMemberId) = await RegisterAndLoginAsync("ra_nonmember2");
        var channelId = await CreateChannelAndJoinAsync(ownerToken, ownerToken, nonMemberId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "no-join",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L,
        });
        var ch = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var newChannelId = ch.GetProperty("channelId").GetGuid();
        var roleId = await CreateRoleAsync(ownerToken, newChannelId);

        var resp = await _client.PostAsync($"/api/v1/channels/{newChannelId}/members/{nonMemberId}/roles/{roleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignRole_AlreadyAssigned_Returns409()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("ra_owner3");
        var (memberToken, memberId) = await RegisterAndLoginAsync("ra_member3");
        var channelId = await CreateChannelAndJoinAsync(ownerToken, memberToken, memberId);
        var roleId = await CreateRoleAsync(ownerToken, channelId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await _client.PostAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);
        var resp = await _client.PostAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RevokeRole_AssignedRole_Returns204()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("ra_owner4");
        var (memberToken, memberId) = await RegisterAndLoginAsync("ra_member4");
        var channelId = await CreateChannelAndJoinAsync(ownerToken, memberToken, memberId);
        var roleId = await CreateRoleAsync(ownerToken, channelId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await _client.PostAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RevokeRole_NotAssigned_Returns404()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("ra_owner5");
        var (memberToken, memberId) = await RegisterAndLoginAsync("ra_member5");
        var channelId = await CreateChannelAndJoinAsync(ownerToken, memberToken, memberId);
        var roleId = await CreateRoleAsync(ownerToken, channelId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
