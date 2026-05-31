using System.Text.Json;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly ApplicationDbContext _db;

    public OutboxWriter(ApplicationDbContext db) => _db = db;

    public void Queue(string topic, string eventType, Guid aggregateId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType());
        _db.OutboxMessages.Add(OutboxMessage.Create(aggregateId, topic, eventType, json));
    }

    public async Task WriteAsync(string topic, string eventType, Guid aggregateId, object payload, CancellationToken ct = default)
    {
        Queue(topic, eventType, aggregateId, payload);
        await _db.SaveChangesAsync(ct);
    }
}
