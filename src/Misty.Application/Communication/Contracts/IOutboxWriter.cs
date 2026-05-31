namespace Misty.Application.Communication.Contracts;

// Writes rows into msg.OutboxMessage for the OutboxRelayWorker to publish to Service Bus.
// Queue does not call SaveChanges, so the row is committed atomically with whatever repository call follows in the same scoped DbContext.
// WriteAsync queues and saves in one transaction, for handlers that don't otherwise mutate state.
public interface IOutboxWriter
{
    void Queue(string topic, string eventType, Guid aggregateId, object payload);
    Task WriteAsync(string topic, string eventType, Guid aggregateId, object payload, CancellationToken ct = default);
}
