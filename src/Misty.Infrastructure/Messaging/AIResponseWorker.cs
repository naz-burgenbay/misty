using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Application.Communication.Contracts;
using Misty.Application.Messaging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;
using System.Text.Json;

namespace Misty.Infrastructure.Messaging;

public sealed class AIResponseWorker : BackgroundService
{
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

        if (!payload.ChannelId.HasValue)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        if (payload.AuthorId == AiUserId)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var messageRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var channel = await db.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == payload.ChannelId.Value, args.CancellationToken);

        if (channel is null || !channel.IsAiAssistantEnabled)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

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

        outbox.Queue(
            MessageEventTopics.Message,
            MessageEventTypes.MessageCreated,
            aiMessage.Id,
            new MessageCreatedPayload(
                aiMessage.Id,
                aiMessage.ChannelId,
                aiMessage.ConversationId,
                aiMessage.AuthorId,
                aiMessage.Content,
                aiMessage.ParentMessageId,
                aiMessage.CreatedAt));

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
