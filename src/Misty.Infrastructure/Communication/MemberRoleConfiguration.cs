using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class MemberRoleConfiguration : IEntityTypeConfiguration<MemberRole>
{
    public void Configure(EntityTypeBuilder<MemberRole> builder)
    {
        builder.ToTable("MemberRole", SchemaNames.Comm);

        builder.HasKey(mr => new { mr.MembershipId, mr.RoleId });

        builder.HasOne<Membership>()
            .WithMany()
            .HasForeignKey(mr => mr.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ChannelRole>()
            .WithMany()
            .HasForeignKey(mr => mr.RoleId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
