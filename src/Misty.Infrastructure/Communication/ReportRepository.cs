using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ReportRepository : IReportRepository
{
    private readonly ApplicationDbContext _db;

    public ReportRepository(ApplicationDbContext db) => _db = db;

    public Task<Report?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Reports.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Report>> GetPendingAsync(int skip = 0, int take = 50, CancellationToken ct = default)
        => await _db.Reports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(Report report, CancellationToken ct = default)
    {
        await _db.Reports.AddAsync(report, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
