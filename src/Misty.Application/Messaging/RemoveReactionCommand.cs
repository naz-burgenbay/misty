using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Messaging;

public record RemoveReactionCommand(
    Guid ChannelId,
    Guid MessageId,
    Guid UserId,
    string EmojiCode)
    : IRequest;

public sealed class RemoveReactionCommandHandler : IRequestHandler<RemoveReactionCommand>
{
    private readonly IMessageRepository _messages;
    private readonly IReactionRepository _reactions;
    private readonly IPermissionService _permissions;

    public RemoveReactionCommandHandler(
        IMessageRepository messages,
        IReactionRepository reactions,
        IPermissionService permissions)
    {
        _messages = messages;
        _reactions = reactions;
        _permissions = permissions;
    }

    public async Task Handle(RemoveReactionCommand request, CancellationToken ct)
    {
        var canSend = await _permissions.CheckPermissionAsync(
            request.UserId,
            request.ChannelId,
            ChannelPermission.AddReactions,
            ct);

        if (!canSend)
            throw new ForbiddenException("You do not have permission to react to messages in this channel.");

        var message = await _messages.GetByIdAsync(request.MessageId, ct);
        if (message is null)
            throw new NotFoundException("Message not found.");
        if (message.ChannelId != request.ChannelId)
            throw new NotFoundException("Message not found.");
        if (message.IsDeleted)
            throw new ValidationException("Cannot modify reactions on a deleted message.");

        // Idempotent remove: missing reaction is a no-op.
        var existing = await _reactions.GetAsync(request.MessageId, request.UserId, request.EmojiCode, ct);
        if (existing is null)
            return;

        await _reactions.RemoveAsync(existing, message.ChannelId, ct);
    }
}
