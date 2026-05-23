namespace Misty.Domain.Communication;

[Flags]
public enum ChannelPermission : long
{
    None             = 0,

    // Reading
    ViewChannel      = 1L << 0,
    ReadHistory      = 1L << 1,

    // Writing
    SendMessages     = 1L << 2,
    AttachFiles      = 1L << 3,
    AddReactions     = 1L << 4,
    MentionEveryone  = 1L << 5,

    // Moderation
    ManageMessages   = 1L << 6,
    MuteMembers      = 1L << 7,
    BanMembers       = 1L << 8,
    KickMembers      = 1L << 9,

    // Administration
    ManageChannel    = 1L << 10,
    ManageRoles      = 1L << 11,
    ManageMembers    = 1L << 12,

    All = ViewChannel | ReadHistory | SendMessages | AttachFiles | AddReactions | MentionEveryone
        | ManageMessages | MuteMembers | BanMembers | KickMembers
        | ManageChannel | ManageRoles | ManageMembers,
}
