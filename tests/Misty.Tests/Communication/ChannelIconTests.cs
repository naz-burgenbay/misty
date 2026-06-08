using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Domain.Communication;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class ChannelIconTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ChannelIconTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId, string Username)> RegisterAndLoginAsync(string username)
    {
        var regResp = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });
        var loginBody = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return (loginBody.GetProperty("accessToken").GetString()!, userId, username);
    }

    private async Task<Guid> CreateChannelAsync(string ownerToken)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "ic-" + Guid.NewGuid().ToString("N")[..8],
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = (long)(ChannelPermission.ViewChannel | ChannelPermission.ReadHistory | ChannelPermission.SendMessages),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task JoinAsync(string token, Guid channelId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { })).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    private static MultipartFormDataContent BuildPngForm()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAfbLI3wAAAABJRU5ErkJggg==");
        var bytes = new ByteArrayContent(png);
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { bytes, "file", "icon.png" } };
    }

    private async Task<HttpResponseMessage> UploadAsync(string token, Guid channelId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var form = BuildPngForm();
        form.Add(new StringContent(await CurrentChannelVersionAsync(channelId)), "version");
        return await _client.PostAsync($"/api/v1/channels/{channelId}/icon", form);
    }

    private async Task<string> CurrentChannelVersionAsync(Guid channelId)
    {
        await using var db = _factory.CreateDbContext();
        var ch = await db.Channels.IgnoreQueryFilters().FirstAsync(c => c.Id == channelId);
        return Convert.ToBase64String(ch.Version);
    }

    private async Task<HttpResponseMessage> SendDeleteIconAsync(Guid channelId, string version)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/channels/{channelId}/icon")
        {
            Content = JsonContent.Create(new { Version = version }),
        };
        return await _client.SendAsync(req);
    }

    private BlobContainerClient IconsContainer()
        => _factory.Services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient("channel-icons");

    [Fact]
    public async Task Upload_AndDelete_WithoutManageChannel_Returns403()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("icon_np_owner");
        var (memberToken, _, _) = await RegisterAndLoginAsync("icon_np_member");

        var channelId = await CreateChannelAsync(ownerToken);
        await JoinAsync(memberToken, channelId);

        var upload = await UploadAsync(memberToken, channelId);
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var del = await SendDeleteIconAsync(channelId, "AAAAAAAAAAA=");
        del.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upload_ReturnsIconUrl_RoundtripsThroughGet_AndBlobExists()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("icon_up_owner");
        var channelId = await CreateChannelAsync(ownerToken);

        var upload = await UploadAsync(ownerToken, channelId);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadBody = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var iconUrl = uploadBody.GetProperty("iconUrl").GetString();
        iconUrl.Should().NotBeNullOrWhiteSpace();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var get = await _client.GetAsync($"/api/v1/channels/{channelId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        getBody.GetProperty("iconUrl").GetString().Should().Be(iconUrl);

        var container = IconsContainer();
        var blobs = new List<string>();
        await foreach (var b in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, $"{channelId}/", default))
            blobs.Add(b.Name);
        blobs.Should().HaveCount(1);
    }

    [Fact]
    public async Task Delete_ClearsIconUrl_AndRemovesBlob()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("icon_del_owner");
        var channelId = await CreateChannelAsync(ownerToken);

        (await UploadAsync(ownerToken, channelId)).StatusCode.Should().Be(HttpStatusCode.OK);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var del = await SendDeleteIconAsync(channelId, await CurrentChannelVersionAsync(channelId));
        del.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await _client.GetAsync($"/api/v1/channels/{channelId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("iconUrl").ValueKind.Should().Be(JsonValueKind.Null);

        var container = IconsContainer();
        var blobs = new List<string>();
        await foreach (var b in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, $"{channelId}/", default))
            blobs.Add(b.Name);
        blobs.Should().BeEmpty();
    }
}
