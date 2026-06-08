using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Messaging;

public record EditMessageCommand(
    Guid MessageId,
    Guid ChannelId,
    Guid UserId,
    string NewContent,
    string Version)
    : IRequest<string>;

public sealed class EditMessageCommandHandler : IRequestHandler<EditMessageCommand, string>
{
    private readonly IMessageRepository _messages;
    private readonly IPermissionService _permissions;
    private readonly IConversationRepository _conversations;
    private readonly IOutboxWriter _outbox;

    public EditMessageCommandHandler(
        IMessageRepository messages,
        IPermissionService permissions,
        IConversationRepository conversations,
        IOutboxWriter outbox)
    {
        _messages = messages;
        _permissions = permissions;
        _conversations = conversations;
        _outbox = outbox;
    }

    public async Task<string> Handle(EditMessageCommand request, CancellationToken ct)
    {
        var message = await _messages.GetByIdAsync(request.MessageId, ct);

        if (message is null)
            throw new NotFoundException("Message not found.");

        if (message.AuthorId != request.UserId)
            throw new ForbiddenException("You can only edit your own messages.");

        if (message.IsDeleted)
            throw new ValidationException("Cannot edit a deleted message.");

        if (message.ChannelId is not null)
        {
            var canSend = await _permissions.CheckPermissionAsync(
                request.UserId,
                message.ChannelId.Value,
                ChannelPermission.SendMessages,
                ct);

            if (!canSend)
                throw new ForbiddenException("You do not have permission to edit messages in this channel.");
        }
        else if (message.ConversationId is not null)
        {
            var conversation = await _conversations.GetByIdAsync(message.ConversationId.Value, ct);
            if (conversation is null)
                throw new NotFoundException("Conversation not found.");

            if (conversation.UserAId != request.UserId && conversation.UserBId != request.UserId)
                throw new ForbiddenException("You are not a participant in this conversation.");
        }
        else
        {
            throw new InvalidOperationException("Message must belong to either a channel or conversation.");
        }

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(request.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

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
                message.EditedAt ?? DateTime.UtcNow,
                string.Empty));

        await _messages.UpdateAsync(message, concurrencyToken, ct);

        return Convert.ToBase64String(message.Version);
    }
}

public sealed class EditMessageCommandValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageCommandValidator()
    {
        RuleFor(x => x.NewContent).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Version).NotEmpty();
    }
}
