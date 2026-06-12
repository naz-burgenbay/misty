namespace Misty.Web.Services.Communication;

public record ReportItemDto(
    Guid Id,
    Guid ReporterId,
    string TargetKind,
    Guid TargetId,
    string Reason,
    string Status,
    DateTime CreatedAt);

public interface IReportService
{
    Task SubmitAsync(string targetKind, Guid targetId, string reason, CancellationToken ct = default);
    Task<IReadOnlyList<ReportItemDto>> GetPendingAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task ReviewAsync(Guid reportId, bool approve, CancellationToken ct = default);
}
