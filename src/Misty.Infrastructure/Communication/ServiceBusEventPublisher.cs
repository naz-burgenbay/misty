using Misty.Application.Communication.Contracts;

namespace Misty.Infrastructure.Communication;

// Routes permission-related events through the transactional outbox. The OutboxRelayWorker is the only component that calls Service Bus directly, so an outage of Service Bus can no longer abort an HTTP request mid-mutation (the audit's silent-data-loss concern).
public sealed class ServiceBusEventPublisher : IEventPublisher
{
    private const string MembershipTopic = "membership-events";
    private const string RoleTopic = "role-events";
    private const string ModerationTopic = "moderation-events";

    private readonly IOutboxWriter _outbox;

    public ServiceBusEventPublisher(IOutboxWriter outbox) => _outbox = outbox;

    public Task PublishMembershipChangedAsync(Guid userId, Guid channelId, CancellationToken ct = default)
        => _outbox.WriteAsync(MembershipTopic, "MembershipChanged", channelId, new CacheInvalidationPayload(userId, channelId), ct);

    public Task PublishRoleChangedAsync(Guid? userId, Guid channelId, CancellationToken ct = default)
        => _outbox.WriteAsync(RoleTopic, "RoleChanged", channelId, new CacheInvalidationPayload(userId, channelId), ct);

    public Task PublishModerationActionAppliedAsync(Guid userId, Guid channelId, CancellationToken ct = default)
        => _outbox.WriteAsync(ModerationTopic, "ModerationActionApplied", channelId, new CacheInvalidationPayload(userId, channelId), ct);
}

// Message body shared by all three permission-related topics (membership-events, role-events, moderation-events).
// Public so consumers outside this assembly (e.g. the SignalR broadcast worker in Misty.Api) can deserialize it directly.
public sealed record CacheInvalidationPayload(Guid? UserId, Guid ChannelId);

