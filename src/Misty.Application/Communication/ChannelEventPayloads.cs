namespace Misty.Application.Communication;

public sealed record ChannelCreatedPayload(
    Guid ChannelId,
    Guid CreatedByUserId,
    string Name,
    bool IsPrivate,
    DateTime OccurredAt);

public sealed record ChannelUpdatedPayload(
    Guid ChannelId,
    Guid UpdatedByUserId,
    string Name,
    DateTime OccurredAt);

public sealed record ChannelDeletedPayload(
    Guid ChannelId,
    Guid DeletedByUserId,
    DateTime OccurredAt);

public static class ChannelEventTopics
{
    public const string Channel = "channel-events";
}

public static class ChannelEventTypes
{
    public const string ChannelCreated = "ChannelCreated";
    public const string ChannelUpdated = "ChannelUpdated";
    public const string ChannelDeleted = "ChannelDeleted";
}
