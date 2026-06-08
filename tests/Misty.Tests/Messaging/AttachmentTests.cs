using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Messaging;

[Collection("Integration")]
public sealed class AttachmentTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xA8, 0xE3, 0x35, 0xB2, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public AttachmentTests(ApiFactory factory)
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
            SchemasToInclude = ["users", "comm", "msg"],
        });
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
    {
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
        return (token, userId);
    }

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> CreateChannelAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();
    }

    private async Task JoinChannelAsync(Guid channelId)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created);
    }

    private async Task<Guid> CreateRoleAsync(Guid channelId, string name, long permissions)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = name,
            Permissions = permissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("roleId").GetGuid();
    }

    private async Task AssignRoleAsync(Guid channelId, Guid userId, Guid roleId)
    {
        var resp = await _client.PostAsync(
            $"/api/v1/channels/{channelId}/members/{userId}/roles/{roleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<Guid> SendMessageAsync(Guid channelId, string content, Guid? parentMessageId = null)
    {
        var payload = parentMessageId is null
            ? (object)new { Content = content, IdempotencyKey = Guid.NewGuid().ToString() }
            : new { Content = content, IdempotencyKey = Guid.NewGuid().ToString(), ParentMessageId = parentMessageId };
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();
    }

    private static MultipartFormDataContent MakeFileContent(
        string contentType = "image/png",
        string fileName = "pic.png",
        byte[]? data = null)
    {
        var bytes = data ?? PngBytes;
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return form;
    }

    private Task<HttpResponseMessage> UploadAttachmentAsync(
        Guid channelId, Guid messageId, MultipartFormDataContent form)
        => _client.PostAsync(
            $"/api/v1/channels/{channelId}/messages/{messageId}/attachments", form);

    private BlobServiceClient GetBlobClient()
        => _factory.Services.GetRequiredService<BlobServiceClient>();

    [Fact]
    public async Task UploadAttachment_ToOwnMessage_Returns201AndPersistsRow()
    {
        var (token, userId) = await RegisterAndLoginAsync("att_user1");
        SetToken(token);

        var channelId = await CreateChannelAsync("att-own-ch");
        var msgId = await SendMessageAsync(channelId, "with attachment");

        using var form = MakeFileContent();
        var resp = await UploadAttachmentAsync(channelId, msgId, form);

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, body);

        var json = JsonDocument.Parse(body).RootElement;
        var attachmentId = json.GetProperty("attachmentId").GetGuid();
        json.GetProperty("fileName").GetString().Should().Be("pic.png");
        json.GetProperty("contentType").GetString().Should().Be("image/png");
        json.GetProperty("sizeBytes").GetInt64().Should().Be(PngBytes.Length);
        var cdnUrl = json.GetProperty("cdnUrl").GetString();
        cdnUrl.Should().NotBeNullOrEmpty();
        cdnUrl!.Should().Contain(msgId.ToString());

        await using var db = _factory.CreateDbContext();
        var row = await db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
        row.Should().NotBeNull();
        row!.MessageId.Should().Be(msgId);
        row.AvatarUserId.Should().BeNull();
        row.ChannelIconChannelId.Should().BeNull();

        var blob = GetBlobClient().GetBlobContainerClient(row.BlobContainer).GetBlobClient(row.BlobName);
        (await blob.ExistsAsync()).Value.Should().BeTrue();
    }

    [Fact]
    public async Task UploadAttachment_ToOthersMessage_Returns403()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("att_userA");
        SetToken(tokenA);
        var channelId = await CreateChannelAsync("att-others-ch");
        var roleId = await CreateRoleAsync(channelId, "Contrib", 15L);

        var (tokenB, userBId) = await RegisterAndLoginAsync("att_userB");
        SetToken(tokenB);
        await JoinChannelAsync(channelId);

        SetToken(tokenA);
        await AssignRoleAsync(channelId, userBId, roleId);

        SetToken(tokenB);
        var msgId = await SendMessageAsync(channelId, "B's message");

        SetToken(tokenA);
        using var form = MakeFileContent();
        var resp = await UploadAttachmentAsync(channelId, msgId, form);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadAttachment_ToNonexistentMessage_Returns404()
    {
        var (token, _) = await RegisterAndLoginAsync("att_user_nf");
        SetToken(token);
        var channelId = await CreateChannelAsync("att-nf-ch");

        using var form = MakeFileContent();
        var resp = await UploadAttachmentAsync(channelId, Guid.NewGuid(), form);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadAttachment_ToTombstone_Returns422()
    {
        var (token, _) = await RegisterAndLoginAsync("att_user_tomb");
        SetToken(token);
        var channelId = await CreateChannelAsync("att-tomb-ch");

        var parentId = await SendMessageAsync(channelId, "parent");
        await SendMessageAsync(channelId, "child", parentMessageId: parentId);
        string delVer;
        await using (var db0 = _factory.CreateDbContext())
            delVer = Convert.ToBase64String((await db0.Messages.FirstAsync(m => m.Id == parentId)).Version);
        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/channels/{channelId}/messages/{parentId}")
        {
            Content = JsonContent.Create(new { Version = delVer }),
        };
        var del = await _client.SendAsync(delReq);
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var form = MakeFileContent();
        var resp = await UploadAttachmentAsync(channelId, parentId, form);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UploadAttachment_WithoutAttachFilesPerm_Returns403()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("att_no_perm_A");
        SetToken(tokenA);
        var channelId = await CreateChannelAsync("att-noperm-ch");
        var roleId = await CreateRoleAsync(channelId, "Sender", 7L);

        var (tokenB, userBId) = await RegisterAndLoginAsync("att_no_perm_B");
        SetToken(tokenB);
        await JoinChannelAsync(channelId);

        SetToken(tokenA);
        await AssignRoleAsync(channelId, userBId, roleId);

        SetToken(tokenB);
        var msgId = await SendMessageAsync(channelId, "B msg");

        using var form = MakeFileContent();
        var resp = await UploadAttachmentAsync(channelId, msgId, form);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAttachment_ByOwner_RemovesRowAndBlob()
    {
        var (token, _) = await RegisterAndLoginAsync("att_del_owner");
        SetToken(token);
        var channelId = await CreateChannelAsync("att-del-ch");
        var msgId = await SendMessageAsync(channelId, "to delete");

        using var form = MakeFileContent();
        var up = await UploadAttachmentAsync(channelId, msgId, form);
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        var attachmentId = (await up.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attachmentId").GetGuid();

        string blobContainer;
        string blobName;
        await using (var db = _factory.CreateDbContext())
        {
            var row = await db.Attachments.SingleAsync(a => a.Id == attachmentId);
            blobContainer = row.BlobContainer;
            blobName = row.BlobName;
        }

        var del = await _client.DeleteAsync($"/api/v1/attachments/{attachmentId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = _factory.CreateDbContext())
        {
            (await db.Attachments.AnyAsync(a => a.Id == attachmentId)).Should().BeFalse();
        }

        var blob = GetBlobClient().GetBlobContainerClient(blobContainer).GetBlobClient(blobName);
        (await blob.ExistsAsync()).Value.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAttachment_ByOther_Returns403()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("att_del_other_A");
        SetToken(tokenA);
        var channelId = await CreateChannelAsync("att-delother-ch");
        var msgId = await SendMessageAsync(channelId, "A's message");

        using var form = MakeFileContent();
        var up = await UploadAttachmentAsync(channelId, msgId, form);
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        var attachmentId = (await up.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attachmentId").GetGuid();

        var (tokenB, _) = await RegisterAndLoginAsync("att_del_other_B");
        SetToken(tokenB);
        await JoinChannelAsync(channelId);

        var del = await _client.DeleteAsync($"/api/v1/attachments/{attachmentId}");
        del.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAttachment_ByModerator_Succeeds()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("att_mod_A");
        SetToken(tokenA);
        var channelId = await CreateChannelAsync("att-mod-ch");
        var msgId = await SendMessageAsync(channelId, "A's msg");
        using var form = MakeFileContent();
        var up = await UploadAttachmentAsync(channelId, msgId, form);
        var attachmentId = (await up.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attachmentId").GetGuid();

        var roleId = await CreateRoleAsync(channelId, "Mod", 515L);

        var (tokenB, userBId) = await RegisterAndLoginAsync("att_mod_B");
        SetToken(tokenB);
        await JoinChannelAsync(channelId);

        SetToken(tokenA);
        await AssignRoleAsync(channelId, userBId, roleId);

        SetToken(tokenB);
        var del = await _client.DeleteAsync($"/api/v1/attachments/{attachmentId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        (await db.Attachments.AnyAsync(a => a.Id == attachmentId)).Should().BeFalse();
    }
}
