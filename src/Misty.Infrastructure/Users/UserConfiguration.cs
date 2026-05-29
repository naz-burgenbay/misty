using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Users;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Users;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("User", SchemaNames.Users);

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.DisplayName)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(u => u.Bio)
            .HasColumnType("nvarchar(max)");

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(512);

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(u => u.Version)
            .IsRowVersion();

        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("UX_User_Username");

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("UX_User_Email");
    }
}
