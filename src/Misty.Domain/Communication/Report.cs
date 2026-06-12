namespace Misty.Domain.Communication;

public enum ReportStatus { Pending, Approved, Disapproved }
public enum ReportTargetKind { User, Channel }

public sealed class Report
{
    private Report() { }

    public Guid Id { get; private set; }
    public Guid ReporterId { get; private set; }
    public ReportTargetKind TargetKind { get; private set; }
    public Guid TargetId { get; private set; }   // UserId or ChannelId
    public string Reason { get; private set; } = null!;
    public ReportStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedByAdminId { get; private set; }

    public static Report Create(Guid id, Guid reporterId, ReportTargetKind targetKind, Guid targetId, string reason)
        => new()
        {
            Id = id,
            ReporterId = reporterId,
            TargetKind = targetKind,
            TargetId = targetId,
            Reason = reason,
            Status = ReportStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

    public void Approve(Guid adminId)
    {
        Status = ReportStatus.Approved;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByAdminId = adminId;
    }

    public void Disapprove(Guid adminId)
    {
        Status = ReportStatus.Disapproved;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByAdminId = adminId;
    }
}
