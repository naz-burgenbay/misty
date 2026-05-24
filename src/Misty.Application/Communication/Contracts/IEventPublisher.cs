namespace Misty.Application.Communication.Contracts;

// Publishes domain events used for cache invalidation and background processing. The interface lives in Application to avoid coupling it to Service Bus infrastructure.
public interface IEventPublisher
{
    Task PublishMembershipChangedAsync(Guid userId, Guid channelId, CancellationToken ct = default);

// Null when the change affects the entire channel rather than a specific user (for example, role permission updates or role deletion).
    Task PublishRoleChangedAsync(Guid? userId, Guid channelId, CancellationToken ct = default);

    Task PublishModerationActionAppliedAsync(Guid userId, Guid channelId, CancellationToken ct = default);
}
