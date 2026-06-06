using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Messaging;

public record DeleteMessageCommand(
    Guid MessageId,
    Guid ChannelId,
    Guid UserId,
    string Version)
    : IRequest;

public sealed class DeleteMessageCommandHandler : IRequestHandler<DeleteMessageCommand>
{
    private readonly IMessageRepository _messages;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public DeleteMessageCommandHandler(
        IMessageRepository messages,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _messages = messages;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task Handle(DeleteMessageCommand request, CancellationToken ct)
    {
        var canSend = await _permissions.CheckPermissionAsync(
            request.UserId,
            request.ChannelId,
            ChannelPermission.SendMessages,
            ct);

        if (!canSend)
            throw new ForbiddenException("You do not have permission to delete messages in this channel.");

        var message = await _messages.GetByIdAsync(request.MessageId, ct);

        if (message is null)
            throw new NotFoundException("Message not found.");

        if (message.AuthorId != request.UserId)
            throw new ForbiddenException("You can only delete your own messages.");

        if (message.IsDeleted)
            return; // Already deleted, idempotent

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(request.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        var hasReplies = await _messages.HasRepliesAsync(request.MessageId, ct);

        if (hasReplies)
        {
            // Tombstone: clear content and mark as deleted
            message.Tombstone();
            _outbox.Queue(
                MessageEventTopics.Message,
                MessageEventTypes.MessageDeleted,
                message.Id,
                new MessageDeletedPayload(
                    message.Id,
                    message.ChannelId,
                    message.ConversationId,
                    IsTombstone: true));
            await _messages.UpdateAsync(message, concurrencyToken, ct);
        }
        else
        {
            // Hard-delete: remove the message entirely
            _outbox.Queue(
                MessageEventTopics.Message,
                MessageEventTypes.MessageDeleted,
                message.Id,
                new MessageDeletedPayload(
                    message.Id,
                    message.ChannelId,
                    message.ConversationId,
                    IsTombstone: false));
            await _messages.DeleteAsync(message, ct);
        }
    }
}
