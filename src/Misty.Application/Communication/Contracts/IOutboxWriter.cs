namespace Misty.Application.Communication.Contracts;

public interface IOutboxWriter
{
    void Queue(string topic, string eventType, Guid aggregateId, object payload);
    Task WriteAsync(string topic, string eventType, Guid aggregateId, object payload, CancellationToken ct = default);
}
