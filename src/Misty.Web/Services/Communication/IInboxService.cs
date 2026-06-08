using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Misty.Web.Services.Common;
using Misty.Web.Services.Realtime;

namespace Misty.Web.Services.Communication;

public sealed record InboxItemDto(
    Guid Id,
    string Type,
    Guid ActorUserId,
    string ActorDisplayName,
    string? ActorAvatarUrl,
    Guid? ReferenceId,
    [property: JsonPropertyName("referencePayload")]
    System.Text.Json.JsonElement? ReferencePayload,
    bool IsActedOn,
    DateTime CreatedAt);

public sealed record InboxPageDto(
    IReadOnlyList<InboxItemDto> Items,
    string? NextCursor);

public interface IInboxService
{
    Observable<IReadOnlyList<InboxItemDto>> Items { get; }
    Observable<int> UnreadCount { get; }

    Task<InboxPageDto> RefreshAsync(string? cursor = null, int take = 25, CancellationToken ct = default);
    Task DismissAsync(Guid itemId, CancellationToken ct = default);
    void RemoveItem(Guid itemId);
}

public sealed class HttpInboxService : IInboxService, IDisposable
{
    private readonly HttpClient _http;
    private readonly IDisposable _signalRSub;
    private readonly object _gate = new();

    public Observable<IReadOnlyList<InboxItemDto>> Items { get; } = new(Array.Empty<InboxItemDto>());
    public Observable<int> UnreadCount { get; } = new(0);

    public HttpInboxService(HttpClient http, ISignalRClient hub)
    {
        _http = http;
        _signalRSub = hub.OnInboxItemReceived(evt => { var _ = RefreshAsync(); });
    }

    public async Task<InboxPageDto> RefreshAsync(string? cursor = null, int take = 25, CancellationToken ct = default)
    {
        var url = $"api/v1/inbox?take={take}";
        if (cursor is not null) url += $"&cursor={Uri.EscapeDataString(cursor)}";

        var page = await _http.GetFromJsonAsync<InboxPageDto>(url, ct)
                   ?? new InboxPageDto(Array.Empty<InboxItemDto>(), null);

        List<InboxItemDto> merged;
        lock (_gate)
        {
            if (cursor is null)
            {
                merged = page.Items.ToList();
            }
            else
            {
                var existing = Items.Value.ToList();
                var existingIds = existing.Select(i => i.Id).ToHashSet();
                merged = existing.Concat(page.Items.Where(i => !existingIds.Contains(i.Id))).ToList();
            }
        }

        Items.Set(merged);
        UnreadCount.Set(merged.Count(i => !i.IsActedOn));
        return page;
    }

    public async Task DismissAsync(Guid itemId, CancellationToken ct = default)
    {
        var before = Items.Value;
        var item = before.FirstOrDefault(i => i.Id == itemId);
        var after = before.Where(i => i.Id != itemId).ToList();
        Items.Set(after);
        if (item is { IsActedOn: false })
            UnreadCount.Set(Math.Max(0, UnreadCount.Value - 1));

        try
        {
            using var resp = await _http.PostAsync($"api/v1/inbox/{itemId}/dismiss", content: null, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch
        {
            Items.Set(before.ToList());
            if (item is { IsActedOn: false })
                UnreadCount.Set(UnreadCount.Value + 1);
            throw;
        }
    }

    public void RemoveItem(Guid itemId)
    {
        var before = Items.Value;
        var item = before.FirstOrDefault(i => i.Id == itemId);
        var after = before.Where(i => i.Id != itemId).ToList();
        Items.Set(after);
        if (item is { IsActedOn: false })
            UnreadCount.Set(Math.Max(0, UnreadCount.Value - 1));
    }

    public void Dispose() => _signalRSub.Dispose();
}

