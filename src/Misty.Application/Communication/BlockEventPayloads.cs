namespace Misty.Application.Communication;

public sealed record UserBlockedPayload(
    Guid BlockerId,
    Guid BlockedId,
    DateTime OccurredAt);

public sealed record UserUnblockedPayload(
    Guid BlockerId,
    Guid BlockedId,
    DateTime OccurredAt);

public static class BlockEventTopics
{
    public const string Block = "block-events";
}

public static class BlockEventTypes
{
    public const string UserBlocked = "UserBlocked";
    public const string UserUnblocked = "UserUnblocked";
}
