namespace Misty.Application.Communication;

// Message body shared by the three permission-related topics (membership-events, role-events, moderation-events).
// Public so consumers in Misty.Api (PermissionEventsBroadcastWorker) and Misty.Infrastructure (CacheInvalidationWorker) can deserialize it directly.
// Will be replaced by per-transition typed records in Phase 9 Step 9.1.b.
public sealed record CacheInvalidationPayload(Guid? UserId, Guid ChannelId);
