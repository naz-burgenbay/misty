using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _db;

    public ConversationRepository(ApplicationDbContext db) => _db = db;

    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Conversation?> GetByUsersAsync(Guid userAId, Guid userBId, CancellationToken ct = default)
        => _db.Conversations.FirstOrDefaultAsync(
            c => c.UserAId == userAId && c.UserBId == userBId, ct);

    public async Task<IReadOnlyList<Conversation>> GetForUserAsync(Guid userId, CancellationToken ct = default)
        => await _db.Conversations
            .AsNoTracking()
            .Where(c => c.UserAId == userId || c.UserBId == userId)
            .ToListAsync(ct);

    public async Task AddAsync(Conversation conversation, CancellationToken ct = default)
    {
        await _db.Conversations.AddAsync(conversation, ct);
        await _db.SaveChangesAsync(ct);
    }
}
