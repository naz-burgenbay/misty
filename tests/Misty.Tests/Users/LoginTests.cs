using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Users;

[Collection("Integration")]
public sealed class LoginTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public LoginTests(ApiFactory factory)
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
            SchemasToInclude = ["users"],
        });
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithToken()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "loginuser",
            DisplayName = "Login User",
            Password = "Str0ngPass!",
        });

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "loginuser",
            Password = "Str0ngPass!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        token.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Subject.Should().NotBeNullOrEmpty();
        jwt.Claims.Should().Contain(c => c.Type == "preferred_username" && c.Value == "loginuser");
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "wrongpass",
            DisplayName = "Wrong Pass",
            Password = "Str0ngPass!",
        });

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "wrongpass",
            Password = "NotTheRightPass!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonexistentUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "ghost",
            Password = "Str0ngPass!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidToken_Returns200WithUserInfo()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "meuser",
            DisplayName = "Me User",
            Password = "Str0ngPass!",
        });

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "meuser",
            Password = "Str0ngPass!",
        });

        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;

        // Use a fresh client so shared _client is unaffected by auth header.
        using var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var meResp = await authedClient.GetAsync("/api/v1/auth/me");

        meResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meBody = await meResp.Content.ReadFromJsonAsync<JsonElement>();
        meBody.GetProperty("username").GetString().Should().Be("meuser");
        meBody.GetProperty("userId").GetGuid().Should().NotBeEmpty();
    }
}
