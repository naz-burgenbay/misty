using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Auth;
using Misty.Web.Services.Common;
using Misty.Web.Services.Users;

namespace Misty.Web.Services.Communication;

public sealed record DirectConversationDto(
    Guid Id,
    Guid OtherUserId);

internal sealed record CreateConversationRequestDto(Guid OtherUserId);
internal sealed record CreateConversationResponseDto(Guid ConversationId);
internal sealed record ConversationWireDto(Guid ConversationId, Guid OtherUserId);

public interface IDirectMessageService
{
    Observable<IReadOnlyList<DirectConversationDto>> MyConversations { get; }

    Task RefreshAsync(CancellationToken ct = default);
    Task<DirectConversationDto> CreateOrGetAsync(Guid otherUserId, CancellationToken ct = default);
    DirectConversationDto? GetCached(Guid conversationId);
}

public sealed class HttpDirectMessageService : IDirectMessageService
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly IUserDirectory _users;
    private readonly ILogger<HttpDirectMessageService> _logger;

    public Observable<IReadOnlyList<DirectConversationDto>> MyConversations { get; } =
        new(Array.Empty<DirectConversationDto>());

    public HttpDirectMessageService(
        HttpClient http,
        IAuthService auth,
        IUserDirectory users,
        ILogger<HttpDirectMessageService> logger)
    {
        _http = http;
        _auth = auth;
        _users = users;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<ConversationWireDto>>("api/v1/conversations", ct)
                       ?? new List<ConversationWireDto>();
            var mapped = list
                .Select(c => new DirectConversationDto(c.ConversationId, c.OtherUserId))
                .ToList();
            MyConversations.Set(mapped);
            foreach (var c in mapped)
                _ = _users.EnsureAsync(c.OtherUserId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            MyConversations.Set(Array.Empty<DirectConversationDto>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load direct conversations.");
            throw;
        }
    }

    public async Task<DirectConversationDto> CreateOrGetAsync(Guid otherUserId, CancellationToken ct = default)
    {
        var me = _auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Not signed in.");
        if (otherUserId == me)
            throw new InvalidOperationException("Cannot start a conversation with yourself.");

        var (_, _) = me.CompareTo(otherUserId) < 0 ? (me, otherUserId) : (otherUserId, me);

        using var resp = await _http.PostAsJsonAsync("api/v1/conversations",
            new CreateConversationRequestDto(otherUserId), ct);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<CreateConversationResponseDto>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty create conversation response.");

        var dto = new DirectConversationDto(created.ConversationId, otherUserId);

        var next = new List<DirectConversationDto>(MyConversations.Value.Count + 1) { dto };
        next.AddRange(MyConversations.Value.Where(c => c.Id != dto.Id));
        MyConversations.Set(next);

        _ = _users.EnsureAsync(otherUserId);
        return dto;
    }

    public DirectConversationDto? GetCached(Guid conversationId)
        => MyConversations.Value.FirstOrDefault(c => c.Id == conversationId);
}
