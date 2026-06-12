using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Report", SchemaNames.Comm);

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReporterId).IsRequired();
        builder.Property(r => r.TargetId).IsRequired();

        builder.Property(r => r.TargetKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasIndex(r => r.Status)
            .HasDatabaseName("IX_Report_Status");

        builder.HasIndex(r => new { r.Status, r.CreatedAt })
            .HasDatabaseName("IX_Report_Status_CreatedAt");
    }
}
