using System.Buffers.Text;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class InboxItemRepository : IInboxItemRepository
{
    private readonly ApplicationDbContext _db;

    public InboxItemRepository(ApplicationDbContext db) => _db = db;

    public Task<InboxItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.InboxItems.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<bool> ExistsAsync(Guid userId, InboxItemType type, Guid? referenceId, CancellationToken ct = default)
        => _db.InboxItems.AnyAsync(
            i => i.UserId == userId && i.Type == type && i.ReferenceId == referenceId,
            ct);

    public async Task<(IReadOnlyList<InboxItem> Items, string? NextCursor)> GetPageAsync(
        Guid userId,
        string? cursor,
        int take,
        CancellationToken ct = default)
    {
        var query = _db.InboxItems.AsNoTracking().Where(i => i.UserId == userId);

        if (TryDecodeCursor(cursor, out var afterTicks, out var afterId))
        {
            var afterCreatedAt = new DateTime(afterTicks, DateTimeKind.Utc);
            query = query.Where(i =>
                i.CreatedAt < afterCreatedAt
                || (i.CreatedAt == afterCreatedAt && i.Id.CompareTo(afterId) < 0));
        }

        var page = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (page.Count > take)
        {
            var last = page[take - 1];
            nextCursor = EncodeCursor(last.CreatedAt.Ticks, last.Id);
            page.RemoveAt(page.Count - 1);
        }

        return (page, nextCursor);
    }

    public async Task AddAsync(InboxItem item, CancellationToken ct = default)
    {
        await _db.InboxItems.AddAsync(item, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(InboxItem item, CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    private static string EncodeCursor(long ticks, Guid id)
    {
        var raw = $"{ticks}:{id:N}";
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
    }

    private static bool TryDecodeCursor(string? cursor, out long ticks, out Guid id)
    {
        ticks = 0;
        id = Guid.Empty;
        if (string.IsNullOrEmpty(cursor)) return false;

        try
        {
            var raw = Encoding.ASCII.GetString(Convert.FromBase64String(cursor));
            var sep = raw.IndexOf(':');
            if (sep <= 0) return false;
            return long.TryParse(raw.AsSpan(0, sep), out ticks)
                && Guid.TryParseExact(raw.AsSpan(sep + 1), "N", out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
