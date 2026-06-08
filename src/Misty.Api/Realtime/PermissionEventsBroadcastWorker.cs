using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Application.Communication;

namespace Misty.Api.Realtime;

public sealed class PermissionEventsBroadcastWorker : BackgroundService
{
    private const string BroadcastSubscription = "realtime-broadcast";

    private readonly ServiceBusClient _client;
    private readonly IHubContext<MistyHub> _hub;
    private readonly ILogger<PermissionEventsBroadcastWorker> _logger;

    public PermissionEventsBroadcastWorker(
        ServiceBusClient client,
        IHubContext<MistyHub> hub,
        ILogger<PermissionEventsBroadcastWorker> logger)
    {
        _client = client;
        _hub = hub;
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
            _client.CreateProcessor("membership-events", BroadcastSubscription, opts);
        await using var roleProcessor =
            _client.CreateProcessor("role-events", BroadcastSubscription, opts);
        await using var moderationProcessor =
            _client.CreateProcessor("moderation-events", BroadcastSubscription, opts);

        membershipProcessor.ProcessMessageAsync += HandleAsync;
        membershipProcessor.ProcessErrorAsync += OnErrorAsync;
        roleProcessor.ProcessMessageAsync += HandleAsync;
        roleProcessor.ProcessErrorAsync += OnErrorAsync;
        moderationProcessor.ProcessMessageAsync += HandleAsync;
        moderationProcessor.ProcessErrorAsync += OnErrorAsync;

        await membershipProcessor.StartProcessingAsync(stoppingToken);
        await roleProcessor.StartProcessingAsync(stoppingToken);
        await moderationProcessor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleAsync(ProcessMessageEventArgs args)
    {
        var eventType = args.Message.Subject;
        BroadcastTarget? target;

        try
        {
            target = ExtractBroadcastTarget(eventType, args.Message.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed {EventType} message; dead-lettering.", eventType);
            await args.DeadLetterMessageAsync(args.Message, "MalformedPayload",
                cancellationToken: args.CancellationToken);
            return;
        }

        if (target is null)
        {
            _logger.LogWarning("Ignoring unknown permission broadcast event type '{EventType}'", eventType);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        try
        {
            var payload = new PermissionInvalidationDto(target.Value.UserId, target.Value.ChannelId);
            if (payload.UserId.HasValue)
            {
                await _hub.Clients
                    .Group($"user:{payload.UserId}")
                    .SendAsync(target.Value.ClientEventName, payload, args.CancellationToken);
            }
            else
            {
                await _hub.Clients
                    .Group($"channel:{payload.ChannelId}")
                    .SendAsync(target.Value.ClientEventName, payload, args.CancellationToken);
            }

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            _logger.LogDebug("Broadcast {ClientEventName} (from {EventType}) for channel {ChannelId} user {UserId}",
                target.Value.ClientEventName, eventType, payload.ChannelId, payload.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast {EventType}; abandoning.", eventType);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private static BroadcastTarget? ExtractBroadcastTarget(string eventType, BinaryData body) => eventType switch
    {
        PermissionEventTypes.MembershipJoined =>
            JsonSerializer.Deserialize<MembershipJoinedPayload>(body) is { } p
                ? new BroadcastTarget(p.UserId, p.ChannelId, "MembershipChanged") : null,
        PermissionEventTypes.MembershipLeft =>
            JsonSerializer.Deserialize<MembershipLeftPayload>(body) is { } p
                ? new BroadcastTarget(p.UserId, p.ChannelId, "MembershipChanged") : null,
        PermissionEventTypes.MembershipKicked =>
            JsonSerializer.Deserialize<MembershipKickedPayload>(body) is { } p
                ? new BroadcastTarget(p.TargetUserId, p.ChannelId, "MembershipChanged") : null,
        PermissionEventTypes.MemberRoleAssigned =>
            JsonSerializer.Deserialize<MemberRoleAssignedPayload>(body) is { } p
                ? new BroadcastTarget(p.TargetUserId, p.ChannelId, "RoleChanged") : null,
        PermissionEventTypes.MemberRoleRevoked =>
            JsonSerializer.Deserialize<MemberRoleRevokedPayload>(body) is { } p
                ? new BroadcastTarget(p.TargetUserId, p.ChannelId, "RoleChanged") : null,
        PermissionEventTypes.ChannelRoleUpdated =>
            JsonSerializer.Deserialize<ChannelRoleUpdatedPayload>(body) is { } p
                ? new BroadcastTarget(null, p.ChannelId, "RoleChanged") : null,
        PermissionEventTypes.ChannelRoleDeleted =>
            JsonSerializer.Deserialize<ChannelRoleDeletedPayload>(body) is { } p
                ? new BroadcastTarget(null, p.ChannelId, "RoleChanged") : null,
        PermissionEventTypes.ModerationActionApplied =>
            JsonSerializer.Deserialize<ModerationActionAppliedPayload>(body) is { } p
                ? new BroadcastTarget(p.TargetUserId, p.ChannelId, "ModerationActionApplied") : null,
        PermissionEventTypes.ModerationActionRevoked =>
            JsonSerializer.Deserialize<ModerationActionRevokedPayload>(body) is { } p
                ? new BroadcastTarget(p.TargetUserId, p.ChannelId, "ModerationActionApplied") : null,
        _ => null,
    };

    private readonly record struct BroadcastTarget(Guid? UserId, Guid ChannelId, string ClientEventName);

    private sealed record PermissionInvalidationDto(Guid? UserId, Guid ChannelId);

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus realtime-broadcast processor error: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
