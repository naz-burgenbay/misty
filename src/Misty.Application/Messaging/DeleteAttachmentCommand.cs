using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public record DeleteAttachmentCommand(Guid UserId, Guid AttachmentId) : IRequest;

public sealed class DeleteAttachmentCommandHandler : IRequestHandler<DeleteAttachmentCommand>
{
    private readonly IAttachmentRepository _attachments;
    private readonly IMessageRepository _messages;
    private readonly IAttachmentStorage _storage;
    private readonly IPermissionService _permissions;

    public DeleteAttachmentCommandHandler(
        IAttachmentRepository attachments,
        IMessageRepository messages,
        IAttachmentStorage storage,
        IPermissionService permissions)
    {
        _attachments = attachments;
        _messages = messages;
        _storage = storage;
        _permissions = permissions;
    }

    public async Task Handle(DeleteAttachmentCommand request, CancellationToken ct)
    {
        var attachment = await _attachments.GetByIdAsync(request.AttachmentId, ct);
        if (attachment is null)
            throw new NotFoundException("Attachment not found.");

        if (attachment.OwnerType != AttachmentOwnerType.Message || attachment.MessageId is null)
            throw new ForbiddenException("This attachment cannot be deleted via this endpoint.");

        var message = await _messages.GetByIdAsync(attachment.MessageId.Value, ct);
        if (message is null)
            throw new NotFoundException("Owning message not found.");

        var isAuthor = message.AuthorId == request.UserId;
        var canManage = isAuthor
            || (message.ChannelId is { } cid
                && await _permissions.CheckPermissionAsync(request.UserId, cid, ChannelPermission.ManageMessages, ct));

        if (!canManage)
            throw new ForbiddenException("You do not have permission to delete this attachment.");

        // Hard-delete: remove the blob first, then the row. If the blob delete fails, the row stays so a retry can finish the job rather than leaving a dangling blob.
        await _storage.DeleteAsync(attachment.BlobContainer, attachment.BlobName, ct);
        await _attachments.RemoveAsync(attachment, ct);
    }
}
