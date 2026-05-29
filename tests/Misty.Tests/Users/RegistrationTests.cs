using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Users;

[Collection("Integration")]
public sealed class RegistrationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public RegistrationTests(ApiFactory factory)
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
    public async Task Register_WithValidData_Returns201AndPersistsUser()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "alice",
            Email = "alice@test.misty",
            DisplayName = "Alice Wonderland",
            Password = "Str0ngPass!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "alice");

        user.Should().NotBeNull();
        user!.PasswordHash.Should().NotBe("Str0ngPass!", "password must not be stored in plaintext");
        user.PasswordHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_WithDuplicateUsername_Returns409()
    {
        var body = new { Username = "duplicate", Email = "duplicate@test.misty", DisplayName = "First", Password = "Str0ngPass!" };

        await _client.PostAsJsonAsync("/api/v1/auth/register", body);
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithInvalidData_Returns422WithErrorDetails()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "",
            Email = "not-an-email",
            DisplayName = "",
            Password = "x",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "firstuser",
            Email = "shared@test.misty",
            DisplayName = "First",
            Password = "Str0ngPass!",
        });
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "seconduser",
            Email = "shared@test.misty",
            DisplayName = "Second",
            Password = "Str0ngPass!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
