namespace Misty.Web.Services.Permissions;

[Flags]
public enum ChannelPermissionFlags : long
{
    None             = 0,

    ViewChannel      = 1L << 0,
    ReadHistory      = 1L << 1,

    SendMessages     = 1L << 2,
    AttachFiles      = 1L << 3,
    AddReactions     = 1L << 4,
    MentionEveryone  = 1L << 5,

    MuteMembers      = 1L << 6,
    KickMembers      = 1L << 7,
    BanMembers       = 1L << 8,
    ManageMessages   = 1L << 9,

    InviteMembers    = 1L << 10,
    ManageMembers    = 1L << 11,
    ManageRoles      = 1L << 12,
    ManageChannel    = 1L << 13,
}

public static class ChannelPermissionFlagsExtensions
{
    private const ChannelPermissionFlags ModerationMask =
        ChannelPermissionFlags.MuteMembers
        | ChannelPermissionFlags.BanMembers
        | ChannelPermissionFlags.KickMembers
        | ChannelPermissionFlags.ManageMessages;

    public static bool CanModerate(this ChannelPermissionFlags flags) =>
        (flags & ModerationMask) != 0;
}
