using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public record AddReactionCommand(
    Guid ChannelId,
    Guid MessageId,
    Guid UserId,
    string EmojiCode)
    : IRequest;

public sealed class AddReactionCommandHandler : IRequestHandler<AddReactionCommand>
{
    private readonly IMessageRepository _messages;
    private readonly IReactionRepository _reactions;
    private readonly IPermissionService _permissions;

    public AddReactionCommandHandler(
        IMessageRepository messages,
        IReactionRepository reactions,
        IPermissionService permissions)
    {
        _messages = messages;
        _reactions = reactions;
        _permissions = permissions;
    }

    public async Task Handle(AddReactionCommand request, CancellationToken ct)
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
            throw new ValidationException("Cannot react to a deleted message.");

        // Idempotent add: existing reaction is a no-op (no outbox event, no error).
        var existing = await _reactions.GetAsync(request.MessageId, request.UserId, request.EmojiCode, ct);
        if (existing is not null)
            return;

        var reaction = MessageReaction.Create(request.MessageId, request.UserId, request.EmojiCode);
        await _reactions.AddAsync(reaction, message.ChannelId, ct);
    }
}

public sealed class AddReactionCommandValidator : AbstractValidator<AddReactionCommand>
{
    public AddReactionCommandValidator()
    {
        RuleFor(x => x.EmojiCode).NotEmpty().MaximumLength(64);
    }
}
