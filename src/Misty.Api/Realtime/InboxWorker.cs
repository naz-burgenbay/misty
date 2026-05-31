using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Application.Communication;
using Misty.Domain.Communication;

namespace Misty.Api.Realtime;

public sealed class InboxWorker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboxWorker> _logger;

    public InboxWorker(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        ILogger<InboxWorker> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        };

        await using var friendProcessor = _client.CreateProcessor(SocialEventTopics.Friend, "inbox-worker", opts);
        await using var inviteProcessor = _client.CreateProcessor(SocialEventTopics.ChannelInvite, "inbox-worker", opts);
        await using var messageProcessor = _client.CreateProcessor(SocialEventTopics.Message, "inbox-worker", opts);

        friendProcessor.ProcessMessageAsync += OnMessageAsync;
        friendProcessor.ProcessErrorAsync += OnErrorAsync;
        inviteProcessor.ProcessMessageAsync += OnMessageAsync;
        inviteProcessor.ProcessErrorAsync += OnErrorAsync;
        messageProcessor.ProcessMessageAsync += OnMessageAsync;
        messageProcessor.ProcessErrorAsync += OnErrorAsync;

        await friendProcessor.StartProcessingAsync(stoppingToken);
        await inviteProcessor.StartProcessingAsync(stoppingToken);
        await messageProcessor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        await friendProcessor.StopProcessingAsync();
        await inviteProcessor.StopProcessingAsync();
        await messageProcessor.StopProcessingAsync();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var eventType = args.Message.Subject;
            var body = args.Message.Body.ToString();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IInboxItemRepository>();

            var item = eventType switch
            {
                SocialEventTypes.FriendRequestSent => MapFriendRequestSent(body),
                SocialEventTypes.FriendRequestAccepted => MapFriendRequestAccepted(body),
                SocialEventTypes.ChannelInviteSent => MapChannelInviteSent(body),
                SocialEventTypes.FirstDirectMessageSent => MapFirstDirectMessageSent(body),
                _ => null,
            };

            if (item is null)
            {
                // Unknown/unrelated event (e.g. MessageCreated on message-events): complete to avoid redelivery loops.
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            if (await repo.ExistsAsync(item.UserId, item.Type, item.ReferenceId, args.CancellationToken))
            {
                await args.CompleteMessageAsync(args.Message);
                _logger.LogDebug("Inbox item already exists for {UserId} {Type} {ReferenceId}; skipped",
                    item.UserId, item.Type, item.ReferenceId);
                return;
            }

            await repo.AddAsync(item, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
            _logger.LogDebug("Inserted inbox item {ItemId} for {UserId} {Type}", item.Id, item.UserId, item.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process inbox-worker message {EventType}", args.Message.Subject);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private static InboxItem? MapFriendRequestSent(string body)
    {
        var p = JsonSerializer.Deserialize<FriendRequestSentPayload>(body);
        if (p is null) return null;
        return InboxItem.Create(Guid.NewGuid(), p.ReceiverId, InboxItemType.FriendRequestReceived, p.SenderId, p.RequestId);
    }

    private static InboxItem? MapFriendRequestAccepted(string body)
    {
        var p = JsonSerializer.Deserialize<FriendRequestAcceptedPayload>(body);
        if (p is null) return null;
        return InboxItem.Create(Guid.NewGuid(), p.OriginalSenderId, InboxItemType.FriendRequestAccepted, p.AccepterId, p.RequestId);
    }

    private static InboxItem? MapChannelInviteSent(string body)
    {
        var p = JsonSerializer.Deserialize<ChannelInviteSentPayload>(body);
        if (p is null) return null;
        return InboxItem.Create(Guid.NewGuid(), p.InvitedUserId, InboxItemType.ChannelInviteReceived, p.InvitedByUserId, p.InviteId);
    }

    private static InboxItem? MapFirstDirectMessageSent(string body)
    {
        var p = JsonSerializer.Deserialize<FirstDirectMessageSentPayload>(body);
        if (p is null) return null;
        return InboxItem.Create(Guid.NewGuid(), p.RecipientId, InboxItemType.FirstDirectMessage, p.SenderId, p.ConversationId);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus inbox-worker processor error: {ErrorSource} on {EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
