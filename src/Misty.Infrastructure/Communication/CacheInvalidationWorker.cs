using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Application.Communication;
using StackExchange.Redis;

namespace Misty.Infrastructure.Communication;

// This worker only depends on singleton services. Workers that require DbContext access will create a separate DI scope per message.
public sealed class CacheInvalidationWorker : BackgroundService
{
    private const string CacheInvalidationSubscription = "cache-invalidation";

    private readonly ServiceBusClient _client;
    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<CacheInvalidationWorker> _logger;

    public CacheInvalidationWorker(
        ServiceBusClient client,
        IConnectionMultiplexer mux,
        ILogger<CacheInvalidationWorker> logger)
    {
        _client = client;
        _mux = mux;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        };

        await using var membershipProcessor =
            _client.CreateProcessor("membership-events", CacheInvalidationSubscription, opts);
        await using var roleProcessor =
            _client.CreateProcessor("role-events", CacheInvalidationSubscription, opts);
        await using var moderationProcessor =
            _client.CreateProcessor("moderation-events", CacheInvalidationSubscription, opts);

        membershipProcessor.ProcessMessageAsync += HandleMessageAsync;
        membershipProcessor.ProcessErrorAsync += HandleErrorAsync;
        roleProcessor.ProcessMessageAsync += HandleMessageAsync;
        roleProcessor.ProcessErrorAsync += HandleErrorAsync;
        moderationProcessor.ProcessMessageAsync += HandleMessageAsync;
        moderationProcessor.ProcessErrorAsync += HandleErrorAsync;

        await membershipProcessor.StartProcessingAsync(stoppingToken);
        await roleProcessor.StartProcessingAsync(stoppingToken);
        await moderationProcessor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Actually expected during application shutdown; processors are disposed automatically.
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var eventType = args.Message.Subject;
        (Guid? userId, Guid channelId)? target;

        try
        {
            target = ExtractInvalidationTarget(eventType, args.Message.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed cache-invalidation message {EventType}; dead-lettering.", eventType);
            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "MalformedPayload",
                cancellationToken: args.CancellationToken);
            return;
        }

        if (target is null)
        {
            // Unknown event type on a permission topic: complete to avoid redelivery loops.
            _logger.LogWarning("Ignoring unknown cache-invalidation event type '{EventType}'", eventType);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        try
        {
            // Redis entries are invalidated before the message is acknowledged.
            await InvalidateCacheAsync(target.Value.userId, target.Value.channelId);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process cache-invalidation message {EventType}; abandoning.", eventType);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private static (Guid? userId, Guid channelId)? ExtractInvalidationTarget(string eventType, BinaryData body)
    {
        switch (eventType)
        {
            case PermissionEventTypes.MembershipJoined:
            {
                var p = JsonSerializer.Deserialize<MembershipJoinedPayload>(body);
                return p is null ? null : (p.UserId, p.ChannelId);
            }
            case PermissionEventTypes.MembershipLeft:
            {
                var p = JsonSerializer.Deserialize<MembershipLeftPayload>(body);
                return p is null ? null : (p.UserId, p.ChannelId);
            }
            case PermissionEventTypes.MembershipKicked:
            {
                var p = JsonSerializer.Deserialize<MembershipKickedPayload>(body);
                return p is null ? null : (p.TargetUserId, p.ChannelId);
            }
            case PermissionEventTypes.MemberRoleAssigned:
            {
                var p = JsonSerializer.Deserialize<MemberRoleAssignedPayload>(body);
                return p is null ? null : (p.TargetUserId, p.ChannelId);
            }
            case PermissionEventTypes.MemberRoleRevoked:
            {
                var p = JsonSerializer.Deserialize<MemberRoleRevokedPayload>(body);
                return p is null ? null : (p.TargetUserId, p.ChannelId);
            }
            case PermissionEventTypes.ChannelRoleUpdated:
            {
                var p = JsonSerializer.Deserialize<ChannelRoleUpdatedPayload>(body);
                return p is null ? null : ((Guid?)null, p.ChannelId);
            }
            case PermissionEventTypes.ChannelRoleDeleted:
            {
                var p = JsonSerializer.Deserialize<ChannelRoleDeletedPayload>(body);
                return p is null ? null : ((Guid?)null, p.ChannelId);
            }
            case PermissionEventTypes.ModerationActionApplied:
            {
                var p = JsonSerializer.Deserialize<ModerationActionAppliedPayload>(body);
                return p is null ? null : (p.TargetUserId, p.ChannelId);
            }
            case PermissionEventTypes.ModerationActionRevoked:
            {
                var p = JsonSerializer.Deserialize<ModerationActionRevokedPayload>(body);
                return p is null ? null : (p.TargetUserId, p.ChannelId);
            }
            default:
                return null;
        }
    }

    private async Task InvalidateCacheAsync(Guid? userId, Guid channelId)
    {
        var redis = _mux.GetDatabase();

        if (userId.HasValue)
        {
            // Single-user invalidation (join, leave, role assign/revoke, moderation action).
            var key = CachedPermissionService.CacheKey(userId.Value, channelId);
            await redis.KeyDeleteAsync(key);
            _logger.LogDebug("Invalidated permission cache: {Key}", key);
        }
        else
        {
            // Channel-wide invalidation (role permission update, role deletion).
            var server = _mux.GetServer(_mux.GetEndPoints()[0]);
            var pattern = $"perm:*:{channelId}";
            var keys = server.KeysAsync(pattern: pattern);
            var count = 0;
            await foreach (var key in keys)
            {
                await redis.KeyDeleteAsync(key);
                count++;
            }

            _logger.LogDebug("Invalidated {Count} permission cache entries for channel {ChannelId}", count, channelId);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus error in source {Source}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
