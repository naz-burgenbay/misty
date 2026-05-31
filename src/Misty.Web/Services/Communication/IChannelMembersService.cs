using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Misty.Web.Services.Communication;

public enum ChannelMemberModerationKind
{
    Mute = 0,
    Ban = 1,
    Warn = 2,
    Kick = 3,
}

public sealed record ChannelMemberDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTime JoinedAt,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<ChannelMemberModerationKind> ActiveModerationTypes);

public interface IChannelMembersService
{
    Task<IReadOnlyList<ChannelMemberDto>> GetMembersAsync(Guid channelId, CancellationToken ct = default);
}

public sealed class HttpChannelMembersService : IChannelMembersService
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpChannelMembersService> _logger;

    public HttpChannelMembersService(HttpClient http, ILogger<HttpChannelMembersService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChannelMemberDto>> GetMembersAsync(Guid channelId, CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<ChannelMemberDto>>(
                $"api/v1/channels/{channelId}/members", ct);
            return list ?? new List<ChannelMemberDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load members for channel {ChannelId}.", channelId);
            return Array.Empty<ChannelMemberDto>();
        }
    }
}
