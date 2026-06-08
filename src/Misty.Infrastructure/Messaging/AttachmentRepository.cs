using Microsoft.EntityFrameworkCore;
using Misty.Application.Messaging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class AttachmentRepository : IAttachmentRepository
{
    private readonly ApplicationDbContext _db;

    public AttachmentRepository(ApplicationDbContext db) => _db = db;

    public Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Attachment>>> GetByMessageIdsAsync(
        IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Attachment>>();

        var rows = await _db.Attachments
            .AsNoTracking()
            .Where(a => a.MessageId != null && messageIds.Contains(a.MessageId!.Value))
            .ToListAsync(ct);

        return rows
            .GroupBy(a => a.MessageId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Attachment>)g.OrderBy(a => a.CreatedAt).ToList());
    }

    public async Task AddAsync(Attachment attachment, CancellationToken ct = default)
    {
        await _db.Attachments.AddAsync(attachment, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Attachment attachment, CancellationToken ct = default)
    {
        _db.Attachments.Remove(attachment);
        await _db.SaveChangesAsync(ct);
    }
}
