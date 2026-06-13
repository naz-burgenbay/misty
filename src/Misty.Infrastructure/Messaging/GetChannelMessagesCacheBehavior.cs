using System.Text.Json;
using MediatR;
using Misty.Application.Messaging;
using StackExchange.Redis;

namespace Misty.Infrastructure.Messaging;

public sealed class GetChannelMessagesCacheBehavior
    : IPipelineBehavior<GetChannelMessagesQuery, GetChannelMessagesResponse>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private readonly IDatabase _redis;

    public GetChannelMessagesCacheBehavior(IConnectionMultiplexer mux)
        => _redis = mux.GetDatabase();

    public async Task<GetChannelMessagesResponse> Handle(
        GetChannelMessagesQuery request,
        RequestHandlerDelegate<GetChannelMessagesResponse> next,
        CancellationToken ct)
    {
        // Key is user-scoped because ReactedByMe is per-user.
        var key = $"msgs:ch:{request.ChannelId}:{request.PageSize}:{request.Cursor}:{request.UserId}";

        var cached = await _redis.StringGetAsync(key);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<GetChannelMessagesResponse>(cached!)!;

        var response = await next();

        await _redis.StringSetAsync(
            key,
            JsonSerializer.Serialize(response),
            Ttl,
            flags: CommandFlags.FireAndForget);

        return response;
    }
}
