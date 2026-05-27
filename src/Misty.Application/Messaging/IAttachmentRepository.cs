using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public interface IAttachmentRepository
{
    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Attachment attachment, CancellationToken ct = default);

    Task RemoveAsync(Attachment attachment, CancellationToken ct = default);
}
