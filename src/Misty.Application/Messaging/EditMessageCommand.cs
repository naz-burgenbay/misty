using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Messaging;

public record EditMessageCommand(
    Guid MessageId,
    Guid ChannelId,
    Guid UserId,
    string NewContent)
    : IRequest;

public sealed class EditMessageCommandHandler : IRequestHandler<EditMessageCommand>
{
    private readonly IMessageRepository _messages;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public EditMessageCommandHandler(
        IMessageRepository messages,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _messages = messages;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task Handle(EditMessageCommand request, CancellationToken ct)
    {
        var canSend = await _permissions.CheckPermissionAsync(
            request.UserId,
            request.ChannelId,
            ChannelPermission.SendMessages,
            ct);

        if (!canSend)
            throw new ForbiddenException("You do not have permission to edit messages in this channel.");

        var message = await _messages.GetByIdAsync(request.MessageId, ct);

        if (message is null)
            throw new NotFoundException("Message not found.");

        if (message.AuthorId != request.UserId)
            throw new ForbiddenException("You can only edit your own messages.");

        if (message.IsDeleted)
            throw new ValidationException("Cannot edit a deleted message.");

        message.Edit(request.NewContent);

        _outbox.Queue(
            MessageEventTopics.Message,
            MessageEventTypes.MessageEdited,
            message.Id,
            new MessageEditedPayload(
                message.Id,
                message.ChannelId,
                message.ConversationId,
                message.Content,
                message.EditedAt ?? DateTime.UtcNow));

        await _messages.UpdateAsync(message, ct);
    }
}

public sealed class EditMessageCommandValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageCommandValidator()
    {
        RuleFor(x => x.NewContent).NotEmpty().MaximumLength(4000);
    }
}
