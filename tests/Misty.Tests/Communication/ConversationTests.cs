using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class ConversationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ConversationTests(ApiFactory factory)
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
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = "Str0ngPass!",
        });
        var loginBody = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return (loginBody.GetProperty("accessToken").GetString()!, userId);
    }

    private async Task<Guid> CreateConversationAsync(string token, Guid otherUserId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/v1/conversations", new { OtherUserId = otherUserId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("conversationId").GetGuid();
    }

    [Fact]
    public async Task Create_AB_ThenBA_ReturnsSameConversationId()
    {
        var (tokenA, userA) = await RegisterAndLoginAsync("conv_user_a");
        var (tokenB, userB) = await RegisterAndLoginAsync("conv_user_b");

        var idFromA = await CreateConversationAsync(tokenA, userB);
        var idFromB = await CreateConversationAsync(tokenB, userA);

        idFromA.Should().Be(idFromB, "canonical ordering means (A,B) and (B,A) resolve to the same conversation");
    }

    [Fact]
    public async Task Create_WithSelf_Returns422()
    {
        var (token, userId) = await RegisterAndLoginAsync("conv_self");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/v1/conversations", new { OtherUserId = userId });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_Idempotent_ReturnsSameIdOnRepeat()
    {
        var (tokenA, userA) = await RegisterAndLoginAsync("conv_idem_a");
        var (_, userB) = await RegisterAndLoginAsync("conv_idem_b");

        var id1 = await CreateConversationAsync(tokenA, userB);
        var id2 = await CreateConversationAsync(tokenA, userB);

        id1.Should().Be(id2, "creating the same conversation twice is idempotent");
    }

    [Fact]
    public async Task GetConversations_ReturnsConversationsWithCorrectOtherUserId()
    {
        var (tokenA, userA) = await RegisterAndLoginAsync("conv_list_a");
        var (tokenB, userB) = await RegisterAndLoginAsync("conv_list_b");
        var (_, userC) = await RegisterAndLoginAsync("conv_list_c");

        await CreateConversationAsync(tokenA, userB);
        await CreateConversationAsync(tokenA, userC);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var resp = await _client.GetAsync("/api/v1/conversations");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var conversations = body.EnumerateArray().ToList();

        conversations.Should().HaveCount(2);
        var otherUserIds = conversations
            .Select(c => c.GetProperty("otherUserId").GetGuid())
            .ToHashSet();
        otherUserIds.Should().Contain(userB);
        otherUserIds.Should().Contain(userC);
    }

    [Fact]
    public async Task GetConversations_WhenNoConversations_ReturnsEmptyList()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("conv_empty_a");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var resp = await _client.GetAsync("/api/v1/conversations");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateArray().Should().BeEmpty();
    }
}
