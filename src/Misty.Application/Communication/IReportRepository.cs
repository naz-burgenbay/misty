using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IReportRepository
{
    Task<Report?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Report>> GetPendingAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task AddAsync(Report report, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
