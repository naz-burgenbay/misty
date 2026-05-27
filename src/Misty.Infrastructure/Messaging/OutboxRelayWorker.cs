using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

// Continuously checks msg.OutboxMessage for events that haven't been published yet, sends each payload to the correct Service Bus topic, and then marks it as published.
// If a DbUpdateConcurrencyException occurs, another instance already processed that row so we skip it and continue with the next message.
public sealed class OutboxRelayWorker : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _client;
    private readonly ILogger<OutboxRelayWorker> _logger;

    // Senders are cached for the lifetime of the worker (ServiceBusClient is thread-safe).
    private readonly Dictionary<string, ServiceBusSender> _senders = new();

    public OutboxRelayWorker(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient client,
        ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in outbox relay batch; retrying after delay");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var outbox in pending)
        {
            await PublishOneAsync(db, outbox, ct);
        }
    }

    private async Task PublishOneAsync(ApplicationDbContext db, OutboxMessage outbox, CancellationToken ct)
    {
        var sender = GetOrCreateSender(outbox.Topic);

        await sender.SendMessageAsync(
            new ServiceBusMessage(BinaryData.FromString(outbox.Payload))
            {
                MessageId = outbox.Id.ToString(),
                Subject = outbox.EventType,
            },
            ct);

        outbox.MarkPublished();

        try
        {
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("Outbox row {OutboxId} published to {Topic}", outbox.Id, outbox.Topic);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogDebug(
                "Outbox row {OutboxId} concurrency conflict; already published by another instance",
                outbox.Id);

            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;
        }
    }

    private ServiceBusSender GetOrCreateSender(string topic)
    {
        if (!_senders.TryGetValue(topic, out var sender))
        {
            sender = _client.CreateSender(topic);
            _senders[topic] = sender;
        }

        return sender;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await Task.WhenAll(_senders.Values.Select(s => s.DisposeAsync().AsTask()));
    }
}
