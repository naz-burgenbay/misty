namespace Misty.Domain.Communication;

public class MemberRole
{
    private MemberRole() { }

    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }

    public static MemberRole Create(Guid membershipId, Guid roleId)
        => new()
        {
            MembershipId = membershipId,
            RoleId = roleId,
        };
}
