// Static fake data

namespace Misty.Web.Services.MockData;

public enum PresenceState { Online, Offline, Away }

public enum MemberRoleKind { Owner, Admin, Member }

public enum ModerationKind { None, Muted, Warned, Banned }

public sealed record MockUser(
    Guid Id,
    string DisplayName,
    string Handle,
    bool IsAi = false,
    PresenceState Presence = PresenceState.Offline,
    string? Bio = null,
    string? LastSeenLabel = null);

public sealed record MockReaction(string Emoji, int Count, bool ReactedByMe);

public sealed record MockMessage(
    Guid Id,
    Guid AuthorId,
    string Content,
    DateTime CreatedAt,
    bool IsTombstone = false,
    bool IsFailed = false,
    bool IsSending = false,
    bool IsEdited = false,
    Guid? ParentMessageId = null,
    IReadOnlyList<MockReaction>? Reactions = null);

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
    public int OnlineCount =>
        Members.Count(m => MockDataStore.GetUser(m.UserId).Presence == PresenceState.Online);
}

public sealed record MockDirectConversation(
    Guid Id,
    Guid OtherUserId,
    string LastPreview,
    DateTime LastAt,
    int UnreadCount = 0,
    bool IsBlocked = false);

public static class MockDataStore
{
    public static readonly Guid MeId          = G("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AlexId        = G("22222222-2222-2222-2222-222222222222");
    public static readonly Guid SamId         = G("33333333-3333-3333-3333-333333333333");
    public static readonly Guid JordanId      = G("44444444-4444-4444-4444-444444444444");
    public static readonly Guid TaylorId      = G("55555555-5555-5555-5555-555555555555");
    public static readonly Guid PriyaId       = G("66666666-6666-6666-6666-666666666666");
    public static readonly Guid MoId          = G("77777777-7777-7777-7777-777777777777");
    public static readonly Guid AssistantId   = G("88888888-8888-8888-8888-888888888888");
    public static readonly Guid BlockedUserId = G("99999999-9999-9999-9999-999999999999");

    public static readonly Guid GeneralChannelId = G("aaaa1111-0000-0000-0000-000000000001");
    public static readonly Guid DesignChannelId  = G("aaaa1111-0000-0000-0000-000000000002");
    public static readonly Guid AiHelpChannelId  = G("aaaa1111-0000-0000-0000-000000000003");
    public static readonly Guid RandomChannelId  = G("aaaa1111-0000-0000-0000-000000000004");

    public static readonly Guid DmAlexId    = G("bbbb1111-0000-0000-0000-000000000001");
    public static readonly Guid DmSamId     = G("bbbb1111-0000-0000-0000-000000000002");
    public static readonly Guid DmTaylorId  = G("bbbb1111-0000-0000-0000-000000000003");
    public static readonly Guid DmBlockedId = G("bbbb1111-0000-0000-0000-000000000004");

    public static IReadOnlyList<MockUser> Users { get; }
    public static IReadOnlyList<MockChannel> Channels { get; }
    public static IReadOnlyList<MockDirectConversation> DirectMessages { get; }
    public static IReadOnlyDictionary<Guid, IReadOnlyList<MockMessage>> MessagesByConversation { get; }

    public static MockUser Me => GetUser(MeId);
    public static MockUser GetUser(Guid id) => _userIndex[id];
    public static MockChannel? GetChannel(Guid id) => Channels.FirstOrDefault(c => c.Id == id);
    public static MockDirectConversation? GetDirect(Guid id) => DirectMessages.FirstOrDefault(d => d.Id == id);

    public static IReadOnlyList<MockMessage> GetMessages(Guid conversationId) =>
        MessagesByConversation.TryGetValue(conversationId, out var msgs) ? msgs : Array.Empty<MockMessage>();

    private static readonly Dictionary<Guid, MockUser> _userIndex;

    static MockDataStore()
    {
        Users = new List<MockUser>
        {
            new(MeId,          "Eliasz",           "@eliasz",     Presence: PresenceState.Online,  Bio: "Building Misty."),
            new(AlexId,        "Alex Morgan",      "@alex",       Presence: PresenceState.Online,  Bio: "Backend + infra."),
            new(SamId,         "Sam Patel",        "@sam",        Presence: PresenceState.Away,    LastSeenLabel: "Away · 12 min"),
            new(JordanId,      "Jordan Reyes",     "@jordan",     Presence: PresenceState.Online),
            new(TaylorId,      "Taylor Kim",       "@taylor",     Presence: PresenceState.Offline, LastSeenLabel: "Last seen 2:34 pm"),
            new(PriyaId,       "Priya Shah",       "@priya",      Presence: PresenceState.Online),
            new(MoId,          "Mo Hassan",        "@mo",         Presence: PresenceState.Offline, LastSeenLabel: "Last seen yesterday"),
            new(AssistantId,   "Misty Assistant",  "@assistant",  IsAi: true,                      Bio: "Per-channel AI assistant."),
            new(BlockedUserId, "Casey Stone",      "@casey",      Presence: PresenceState.Offline),
        };

        _userIndex = Users.ToDictionary(u => u.Id);

        Channels = new List<MockChannel>
        {
            new(GeneralChannelId, "general", IsPrivate: false, IsAiAssistantEnabled: false, UnreadCount: 0,
                Members: new List<MockMembership>
                {
                    new(MeId,     MemberRoleKind.Member),
                    new(AlexId,   MemberRoleKind.Owner),
                    new(SamId,    MemberRoleKind.Admin),
                    new(JordanId, MemberRoleKind.Member),
                    new(TaylorId, MemberRoleKind.Member),
                    new(PriyaId,  MemberRoleKind.Member),
                    new(MoId,     MemberRoleKind.Member, Moderation: ModerationKind.Muted),
                }),
            new(DesignChannelId, "design", IsPrivate: false, IsAiAssistantEnabled: false, UnreadCount: 3,
                Members: new List<MockMembership>
                {
                    new(MeId,     MemberRoleKind.Admin),
                    new(JordanId, MemberRoleKind.Owner),
                    new(PriyaId,  MemberRoleKind.Member),
                }),
            new(AiHelpChannelId, "ai-help", IsPrivate: false, IsAiAssistantEnabled: true, UnreadCount: 0,
                Members: new List<MockMembership>
                {
                    new(MeId,     MemberRoleKind.Owner),
                    new(AlexId,   MemberRoleKind.Member),
                    new(SamId,    MemberRoleKind.Member),
                }),
            new(RandomChannelId, "random", IsPrivate: true, IsAiAssistantEnabled: false, UnreadCount: 12,
                Members: new List<MockMembership>
                {
                    new(MeId,    MemberRoleKind.Member),
                    new(AlexId,  MemberRoleKind.Owner),
                    new(PriyaId, MemberRoleKind.Member),
                }),
        };

        DirectMessages = new List<MockDirectConversation>
        {
            new(DmAlexId,    AlexId,        "Pushed the outbox relay fix — green CI.", Today(9, 41), UnreadCount: 0),
            new(DmSamId,     SamId,         "Sounds good, let's sync at 3.",           Today(8, 12), UnreadCount: 2),
            new(DmTaylorId,  TaylorId,      "Reviewed the design tokens, lgtm.",       Yesterday(17, 28)),
            new(DmBlockedId, BlockedUserId, "(blocked)",                               Yesterday(11, 0), IsBlocked: true),
        };

        var general = BuildGeneralMessages();
        var design = BuildDesignMessages();
        var aiHelp = BuildAiHelpMessages();
        var random = BuildRandomMessages();
        var dmAlex = BuildDmAlexMessages();
        var dmSam = BuildDmSamMessages();
        var dmTaylor = BuildDmTaylorMessages();

        MessagesByConversation = new Dictionary<Guid, IReadOnlyList<MockMessage>>
        {
            [GeneralChannelId] = general,
            [DesignChannelId]  = design,
            [AiHelpChannelId]  = aiHelp,
            [RandomChannelId]  = random,
            [DmAlexId]         = dmAlex,
            [DmSamId]          = dmSam,
            [DmTaylorId]       = dmTaylor,
            [DmBlockedId]      = Array.Empty<MockMessage>(),
        };
    }

    // Message builders. Kept private and inline so the static surface stays one file.

    private static List<MockMessage> BuildGeneralMessages()
    {
        var tombstoned = G("cccc1111-0000-0000-0000-000000000001");
        return new List<MockMessage>
        {
            new(G("c1-1"), AlexId,   "Morning all — quick sync at 10:30?",                       Today(9,  0)),
            new(G("c1-2"), SamId,    "👍 in.",                                                    Today(9,  1)),
            new(G("c1-3"), JordanId, "Same. I'll bring the design notes.",                       Today(9,  2)),
            new(tombstoned, PriyaId, "",                                                          Today(9,  3), IsTombstone: true),
            new(G("c1-5"), MeId,     "Reply to a deleted message:",                              Today(9,  4), ParentMessageId: tombstoned),
            new(G("c1-6"), AlexId,   "Outbox relay is finally green under concurrent load.",     Today(9, 30),
                Reactions: new List<MockReaction> { new("🎉", 3, true), new("🚀", 1, false) }),
            new(G("c1-7"), MeId,     "Beautiful. SignalR test next.",                            Today(9, 32),
                ParentMessageId: G("c1-6")),
            new(G("c1-8"), MoId,     "(muted user note)",                                        Today(9, 33), IsEdited: true),
            new(G("c1-9"), MeId,     "Trying to send this one...",                               Today(9, 41), IsSending: true),
            new(G("c1-a"), MeId,     "This one failed to send.",                                 Today(9, 42), IsFailed: true),
        };
    }

    private static List<MockMessage> BuildDesignMessages() => new()
    {
        new(G("d1-1"), JordanId, "Token reference doc is up.",                                     Today(8, 10)),
        new(G("d1-2"), PriyaId,  "Lovely. The cyan-blue reads much better than purple did.",      Today(8, 12),
            Reactions: new List<MockReaction> { new("💙", 2, false) }),
        new(G("d1-3"), MeId,     "Agreed. Pushed the avatar palette too.",                        Today(8, 14)),
    };

    private static List<MockMessage> BuildAiHelpMessages() => new()
    {
        new(G("a1-1"), MeId,        "What's a good Service Bus retry policy for transient outages?", Today(7,  0)),
        new(G("a1-2"), AssistantId, "For transient outages, exponential backoff with jitter (base 1s, max 30s) and a circuit breaker after 5 consecutive failures works well. Keep the message lock duration generous so the broker doesn't deliver to a second consumer mid-retry.", Today(7,  1)),
        new(G("a1-3"), AlexId,      "+1, that matches what we have in the relay.",                   Today(7,  4)),
    };

    private static List<MockMessage> BuildRandomMessages() => new()
    {
        new(G("r1-1"), AlexId,  "Coffee?",          Yesterday(15, 12)),
        new(G("r1-2"), PriyaId, "Always.",          Yesterday(15, 13)),
    };

    private static List<MockMessage> BuildDmAlexMessages() => new()
    {
        new(G("dm-a-1"), AlexId, "Pushed the outbox relay fix — green CI.",        Today(9, 41)),
        new(G("dm-a-2"), MeId,   "Massive. I'll rebase mine on top.",               Today(9, 42),
            Reactions: new List<MockReaction> { new("🙌", 1, false) }),
    };

    private static List<MockMessage> BuildDmSamMessages() => new()
    {
        new(G("dm-s-1"), MeId,  "Sync at 3 to walk through the moderation flow?",  Today(8, 10)),
        new(G("dm-s-2"), SamId, "Sounds good, let's sync at 3.",                   Today(8, 12)),
    };

    private static List<MockMessage> BuildDmTaylorMessages() => new()
    {
        new(G("dm-t-1"), MeId,    "Tokens are in — mind a quick scan?",            Yesterday(17, 20)),
        new(G("dm-t-2"), TaylorId, "Reviewed the design tokens, lgtm.",             Yesterday(17, 28)),
    };

    // Helpers. Kept terse so the data block above reads as data.

    private static Guid G(string s) =>
        s.Length == 36 ? Guid.Parse(s) : DeterministicGuid(s);

    private static Guid DeterministicGuid(string seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        var src = System.Text.Encoding.UTF8.GetBytes(seed);
        for (var i = 0; i < bytes.Length; i++) bytes[i] = i < src.Length ? src[i] : (byte)(i * 17);
        return new Guid(bytes);
    }

    private static DateTime Today(int hour, int minute) =>
        DateTime.Today.AddHours(hour).AddMinutes(minute);

    private static DateTime Yesterday(int hour, int minute) =>
        DateTime.Today.AddDays(-1).AddHours(hour).AddMinutes(minute);
}
