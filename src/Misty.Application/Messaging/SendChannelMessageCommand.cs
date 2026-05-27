using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public record SendChannelMessageCommand(
    Guid ChannelId,
    Guid AuthorId,
    string Content,
    string IdempotencyKey,
    Guid? ParentMessageId)
    : IRequest<SendMessageResponse>;

public record SendMessageResponse(
    Guid MessageId,
    Guid? ChannelId,
    Guid? ConversationId,
    Guid AuthorId,
    string Content,
    Guid? ParentMessageId,
    bool WasIdempotent,
    DateTime CreatedAt);

public sealed class SendChannelMessageCommandHandler
    : IRequestHandler<SendChannelMessageCommand, SendMessageResponse>
{
    private readonly IMessageRepository _messages;
    private readonly IPermissionService _permissions;

    public SendChannelMessageCommandHandler(
        IMessageRepository messages,
        IPermissionService permissions)
    {
        _messages = messages;
        _permissions = permissions;
    }

    public async Task<SendMessageResponse> Handle(SendChannelMessageCommand request, CancellationToken ct)
    {
        var canSend = await _permissions.CheckPermissionAsync(
            request.AuthorId,
            request.ChannelId,
            ChannelPermission.SendMessages,
            ct);

        if (!canSend)
            throw new ForbiddenException("You do not have permission to send messages in this channel.");

        if (request.ParentMessageId is { } parentId)
        {
            var parent = await _messages.GetByIdAsync(parentId, ct);
            if (parent is null)
                throw new ValidationException("Parent message not found.");
            if (parent.ChannelId != request.ChannelId)
                throw new ValidationException("Parent message does not belong to this channel.");
            if (parent.ParentMessageId is not null)
                throw new ValidationException("Replies to replies are not allowed. Reply to the top-level message instead.");
        }

        var existing = await _messages.FindByIdempotencyKeyAsync(request.AuthorId, request.IdempotencyKey, ct);
        if (existing is not null)
            return ToResponse(existing, wasIdempotent: true);

        var message = Message.CreateForChannel(
            Guid.NewGuid(),
            request.ChannelId,
            request.AuthorId,
            request.Content,
            request.IdempotencyKey,
            request.ParentMessageId);

        await _messages.AddAsync(message, ct);

        return ToResponse(message, wasIdempotent: false);
    }

    private static SendMessageResponse ToResponse(Message m, bool wasIdempotent)
        => new(m.Id, m.ChannelId, m.ConversationId, m.AuthorId, m.Content, m.ParentMessageId, wasIdempotent, m.CreatedAt);
}

public sealed class SendChannelMessageValidator : AbstractValidator<SendChannelMessageCommand>
{
    public SendChannelMessageValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}
