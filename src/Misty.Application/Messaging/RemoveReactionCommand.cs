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

public sealed class RemoveReactionCommandValidator : AbstractValidator<RemoveReactionCommand>
{
    public RemoveReactionCommandValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.EmojiCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class RemoveReactionCommandHandler : IRequestHandler<RemoveReactionCommand>
{
    private readonly IMessageRepository _messages;
    private readonly IReactionRepository _reactions;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public RemoveReactionCommandHandler(
        IMessageRepository messages,
        IReactionRepository reactions,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _messages = messages;
        _reactions = reactions;
        _permissions = permissions;
        _outbox = outbox;
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

        _outbox.Queue(
            MessageEventTopics.Message,
            MessageEventTypes.ReactionRemoved,
            existing.MessageId,
            new ReactionRemovedPayload(
                existing.MessageId,
                message.ChannelId,
                existing.UserId,
                existing.EmojiCode,
                DateTime.UtcNow));

        await _reactions.RemoveAsync(existing, ct);
    }
}
