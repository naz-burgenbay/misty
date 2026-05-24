using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Misty.Infrastructure.Communication;
public sealed class ServiceBusEventPublisher : Application.Communication.Contracts.IEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _membershipSender;
    private readonly ServiceBusSender _roleSender;
    private readonly ServiceBusSender _moderationSender;

    public ServiceBusEventPublisher(ServiceBusClient client)
    {
        _membershipSender = client.CreateSender("membership-events");
        _roleSender = client.CreateSender("role-events");
        _moderationSender = client.CreateSender("moderation-events");
    }

    public Task PublishMembershipChangedAsync(Guid userId, Guid channelId, CancellationToken ct = default)
        => SendAsync(_membershipSender, new CacheInvalidationPayload(userId, channelId), ct);

    public Task PublishRoleChangedAsync(Guid? userId, Guid channelId, CancellationToken ct = default)
        => SendAsync(_roleSender, new CacheInvalidationPayload(userId, channelId), ct);

    public Task PublishModerationActionAppliedAsync(Guid userId, Guid channelId, CancellationToken ct = default)
        => SendAsync(_moderationSender, new CacheInvalidationPayload(userId, channelId), ct);

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            _membershipSender.DisposeAsync().AsTask(),
            _roleSender.DisposeAsync().AsTask(),
            _moderationSender.DisposeAsync().AsTask());
    }

    private static Task SendAsync(ServiceBusSender sender, CacheInvalidationPayload payload, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        return sender.SendMessageAsync(new ServiceBusMessage(body), ct);
    }
}

// (Message body shared by all three cache-invalidation topics)
internal sealed record CacheInvalidationPayload(Guid? UserId, Guid ChannelId);
