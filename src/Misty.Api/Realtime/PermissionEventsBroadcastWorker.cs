using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Infrastructure.Communication;

namespace Misty.Api.Realtime;

// Consumes membership-events, role-events, and moderation-events on the realtime-broadcast subscription (separate from CacheInvalidationWorker's cache-invalidation subscription) and fans them out to connected SignalR clients so the client-side PermissionsCache can invalidate.
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

        membershipProcessor.ProcessMessageAsync += a => HandleAsync(a, "MembershipChanged");
        membershipProcessor.ProcessErrorAsync += OnErrorAsync;
        roleProcessor.ProcessMessageAsync += a => HandleAsync(a, "RoleChanged");
        roleProcessor.ProcessErrorAsync += OnErrorAsync;
        moderationProcessor.ProcessMessageAsync += a => HandleAsync(a, "ModerationActionApplied");
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

    private async Task HandleAsync(ProcessMessageEventArgs args, string eventName)
    {
        CacheInvalidationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CacheInvalidationPayload>(args.Message.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed {EventName} message; dead-lettering.", eventName);
            await args.DeadLetterMessageAsync(args.Message, "MalformedPayload",
                cancellationToken: args.CancellationToken);
            return;
        }

        if (payload is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "NullPayload",
                cancellationToken: args.CancellationToken);
            return;
        }

        try
        {
            // Per-user events (most membership/role/moderation actions) target a single user's tabs/devices via the user:{userId} group set up in MistyHub.OnConnectedAsync.
            // Channel-wide events (e.g. a role permission edit, UserId == null) fan out to every currently connected member of the channel.
            if (payload.UserId.HasValue)
            {
                await _hub.Clients
                    .Group($"user:{payload.UserId}")
                    .SendAsync(eventName, payload, args.CancellationToken);
            }
            else
            {
                await _hub.Clients
                    .Group($"channel:{payload.ChannelId}")
                    .SendAsync(eventName, payload, args.CancellationToken);
            }

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            _logger.LogDebug("Broadcast {EventName} for channel {ChannelId} user {UserId}",
                eventName, payload.ChannelId, payload.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast {EventName}; abandoning.", eventName);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus realtime-broadcast processor error: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
