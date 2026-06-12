using MediatR;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record GetReportsQuery(int Skip = 0, int Take = 50) : IRequest<IReadOnlyList<ReportDto>>;

public record ReportDto(
    Guid Id,
    Guid ReporterId,
    ReportTargetKind TargetKind,
    Guid TargetId,
    string Reason,
    string Status,
    DateTime CreatedAt);

public sealed class GetReportsQueryHandler : IRequestHandler<GetReportsQuery, IReadOnlyList<ReportDto>>
{
    private readonly IReportRepository _reports;

    public GetReportsQueryHandler(IReportRepository reports) => _reports = reports;

    public async Task<IReadOnlyList<ReportDto>> Handle(GetReportsQuery request, CancellationToken ct)
    {
        var reports = await _reports.GetPendingAsync(request.Skip, request.Take, ct);
        return reports.Select(r => new ReportDto(
            r.Id, r.ReporterId, r.TargetKind, r.TargetId, r.Reason, r.Status.ToString(), r.CreatedAt))
            .ToList();
    }
}
