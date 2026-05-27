using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Application.Messaging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;
using System.Text.Json;

namespace Misty.Infrastructure.Messaging;

// Consumes MessageCreated events from the ai-response subscription.
// If the channel has IsAiAssistantEnabled=true, writes an AI reply via the standard message write path (Message + OutboxMessage in one transaction), which fans it out through SignalR automatically.
// A new DI scope is created per message so that the scoped ApplicationDbContext and IMessageRepository are never shared across messages.
public sealed class AIResponseWorker : BackgroundService
{
    // Well-known author ID stamped on every AI-generated message.
    // No corresponding row in users.User is required. AuthorId has no FK in EF config.
    public static readonly Guid AiUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AIResponseWorker> _logger;

    public AIResponseWorker(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        ILogger<AIResponseWorker> logger)
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

        await using var processor = _client.CreateProcessor("message-events", "ai-response", opts);
        processor.ProcessMessageAsync += OnMessageAsync;
        processor.ProcessErrorAsync += OnErrorAsync;
        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        await processor.StopProcessingAsync();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        // The ai-response subscription receives every event published to message-events.
        // Only MessageCreated events are relevant here; skip everything else (e.g. ReactionChanged).
        if (args.Message.Subject != "MessageCreated")
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        MessageCreatedPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MessageCreatedPayload>(args.Message.Body.ToString());
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed AI-response message {MessageId}; dead-lettering.", args.Message.MessageId);
            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "MalformedPayload",
                deadLetterErrorDescription: "JSON deserialization failed",
                cancellationToken: args.CancellationToken);
            return;
        }

        if (payload is null)
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "NullPayload",
                deadLetterErrorDescription: "Deserialization returned null",
                cancellationToken: args.CancellationToken);
            return;
        }

        // Only respond to channel messages
        if (!payload.ChannelId.HasValue)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        // Don't reply to messages already authored by the AI (prevents reply storms).
        if (payload.AuthorId == AiUserId)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var messageRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

        var channel = await db.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == payload.ChannelId.Value, args.CancellationToken);

        if (channel is null || !channel.IsAiAssistantEnabled)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        // Idempotency: redelivery of the same event must not produce a second AI reply.
        var idempotencyKey = $"ai-response:{payload.MessageId}";
        var existing = await messageRepo.FindByIdempotencyKeyAsync(AiUserId, idempotencyKey, args.CancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("AI response for message {MessageId} already written; skipping duplicate.", payload.MessageId);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        var aiMessage = Message.CreateForChannel(
            Guid.NewGuid(),
            payload.ChannelId.Value,
            AiUserId,
            $"[AI] {payload.Content}",
            idempotencyKey);

        await messageRepo.AddAsync(aiMessage, args.CancellationToken);

        _logger.LogInformation("AI response written for message {MessageId} in channel {ChannelId}.",
            payload.MessageId, payload.ChannelId.Value);

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error in AI response processor: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
