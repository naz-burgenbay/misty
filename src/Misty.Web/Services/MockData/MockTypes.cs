namespace Misty.Web.Services.MockData;

public enum MemberRoleKind { Owner, Admin, Member }

public enum ModerationKind { None, Muted, Warned, Banned }

public sealed record MockUser(
    Guid Id,
    string DisplayName,
    string Username,
    bool IsAi = false,
    string? Bio = null);

public sealed record MockReaction(string Emoji, int Count, bool ReactedByMe);

public sealed record MockAttachment(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string CdnUrl);

public sealed record MockParentPreview(
    Guid Id,
    Guid AuthorId,
    string Content,
    bool IsDeleted);

public sealed record MockMessage(
    Guid Id,
    Guid AuthorId,
    string Content,
    DateTime CreatedAt,
    bool IsTombstone = false,
    bool IsEdited = false,
    Guid? ParentMessageId = null,
    MockParentPreview? ParentPreview = null,
    IReadOnlyList<MockReaction>? Reactions = null,
    IReadOnlyList<MockAttachment>? Attachments = null);

public sealed record MockMembership(
    Guid UserId,
    MemberRoleKind Role,
    ModerationKind Moderation = ModerationKind.None);

public sealed record MockChannel(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    int UnreadCount,
    IReadOnlyList<MockMembership> Members)
{
    public int MemberCount => Members.Count;
}

public sealed record MockDirectConversation(
    Guid Id,
    Guid OtherUserId,
    string LastPreview,
    DateTime LastAt,
    int UnreadCount = 0,
    bool IsBlocked = false);

