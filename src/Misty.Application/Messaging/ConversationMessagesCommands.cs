using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public record SendConversationMessageCommand(
    Guid ConversationId,
    Guid AuthorId,
    string Content,
    string IdempotencyKey,
    Guid? ParentMessageId)
    : IRequest<SendMessageResponse>;

public sealed class SendConversationMessageCommandHandler
    : IRequestHandler<SendConversationMessageCommand, SendMessageResponse>
{
    private readonly IMessageRepository _messages;
    private readonly IConversationRepository _conversations;
    private readonly IUserBlockService _blocks;
    private readonly IFriendshipRepository _friendships;
    private readonly IOutboxWriter _outbox;

    public SendConversationMessageCommandHandler(
        IMessageRepository messages,
        IConversationRepository conversations,
        IUserBlockService blocks,
        IFriendshipRepository friendships,
        IOutboxWriter outbox)
    {
        _messages = messages;
        _conversations = conversations;
        _blocks = blocks;
        _friendships = friendships;
        _outbox = outbox;
    }

    public async Task<SendMessageResponse> Handle(SendConversationMessageCommand request, CancellationToken ct)
    {
        var conversation = await _conversations.GetByIdAsync(request.ConversationId, ct)
            ?? throw new NotFoundException("Conversation not found.");

        if (conversation.UserAId != request.AuthorId && conversation.UserBId != request.AuthorId)
            throw new ForbiddenException("You are not a participant in this conversation.");

        var otherUserId = conversation.UserAId == request.AuthorId ? conversation.UserBId : conversation.UserAId;
        if (await _blocks.IsBlockedAsync(request.AuthorId, otherUserId, ct))
            throw new ForbiddenException("You cannot send messages in this conversation.");

        if (request.ParentMessageId is { } parentId)
        {
            var parent = await _messages.GetByIdAsync(parentId, ct);
            if (parent is null)
                throw new ValidationException("Parent message not found.");
            if (parent.ConversationId != request.ConversationId)
                throw new ValidationException("Parent message does not belong to this conversation.");
            if (parent.ParentMessageId is not null)
                throw new ValidationException("Replies to replies are not allowed.");
        }

        var existing = await _messages.FindByIdempotencyKeyAsync(request.AuthorId, request.IdempotencyKey, ct);
        if (existing is not null)
            return ToResponse(existing, wasIdempotent: true);

        // First-DM-between-non-friends inbox trigger: check before insert so the outbox row rides the same SaveChanges as the message.
        var isFirst = !await _messages.AnyForConversationAsync(request.ConversationId, ct);
        if (isFirst && !await _friendships.ExistsAsync(request.AuthorId, otherUserId, ct))
        {
            _outbox.Queue(
                SocialEventTopics.Message,
                SocialEventTypes.ConversationStarted,
                request.ConversationId,
                new ConversationStartedPayload(
                    request.ConversationId, request.AuthorId, otherUserId, DateTime.UtcNow));
        }

        var message = Message.CreateForConversation(
            Guid.NewGuid(),
            request.ConversationId,
            request.AuthorId,
            request.Content,
            request.IdempotencyKey,
            request.ParentMessageId);

        _outbox.Queue(
            MessageEventTopics.Message,
            MessageEventTypes.MessageCreated,
            message.Id,
            new MessageCreatedPayload(
                message.Id,
                message.ChannelId,
                message.ConversationId,
                message.AuthorId,
                message.Content,
                message.ParentMessageId,
                message.CreatedAt));

        await _messages.AddAsync(message, ct);
        return ToResponse(message, wasIdempotent: false);
    }

    private static SendMessageResponse ToResponse(Message m, bool wasIdempotent)
        => new(m.Id, m.ChannelId, m.ConversationId, m.AuthorId, m.Content, m.ParentMessageId, wasIdempotent, m.CreatedAt, Convert.ToBase64String(m.Version));
}

public sealed class SendConversationMessageValidator : AbstractValidator<SendConversationMessageCommand>
{
    public SendConversationMessageValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public record GetConversationMessagesQuery(
    Guid ConversationId,
    Guid UserId,
    int PageSize,
    string? Cursor)
    : IRequest<GetChannelMessagesResponse>;

public sealed class GetConversationMessagesQueryValidator : AbstractValidator<GetConversationMessagesQuery>
{
    public GetConversationMessagesQueryValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        // PageSize is clamped by handler, so no validation needed
    }
}

public sealed class GetConversationMessagesQueryHandler
    : IRequestHandler<GetConversationMessagesQuery, GetChannelMessagesResponse>
{
    private readonly IMessageRepository _messages;
    private readonly IReactionRepository _reactions;
    private readonly IAttachmentRepository _attachments;
    private readonly IConversationRepository _conversations;

    public GetConversationMessagesQueryHandler(
        IMessageRepository messages,
        IReactionRepository reactions,
        IAttachmentRepository attachments,
        IConversationRepository conversations)
    {
        _messages = messages;
        _reactions = reactions;
        _attachments = attachments;
        _conversations = conversations;
    }

    public async Task<GetChannelMessagesResponse> Handle(GetConversationMessagesQuery request, CancellationToken ct)
    {
        var conversation = await _conversations.GetByIdAsync(request.ConversationId, ct)
            ?? throw new NotFoundException("Conversation not found.");

        if (conversation.UserAId != request.UserId && conversation.UserBId != request.UserId)
            throw new ForbiddenException("You are not a participant in this conversation.");

        var (messages, nextCursor) = await _messages.GetByConversationAsync(
            request.ConversationId,
            request.PageSize,
            request.Cursor,
            ct);

        var parentIds = messages
            .Where(m => m.ParentMessageId.HasValue)
            .Select(m => m.ParentMessageId!.Value)
            .Distinct()
            .ToList();

        var parents = parentIds.Count > 0
            ? await _messages.GetByIdsAsync(parentIds, ct)
            : null;

        var messageIds = messages.Select(m => m.Id).ToList();
        var reactionsByMessage = await _reactions.GetAggregatesAsync(messageIds, request.UserId, ct);
        var attachmentsByMessage = await _attachments.GetByMessageIdsAsync(messageIds, ct);

        var dtos = messages.Select(m =>
        {
            ParentPreviewDto? preview = null;
            if (m.ParentMessageId is { } pid && parents is not null && parents.TryGetValue(pid, out var parent))
                preview = new ParentPreviewDto(parent.Id, parent.AuthorId, parent.Content, parent.IsDeleted);

            var reactions = reactionsByMessage.TryGetValue(m.Id, out var aggs)
                ? aggs.Select(a => new ReactionSummaryDto(a.EmojiCode, a.Count, a.ReactedByMe)).ToList()
                : new List<ReactionSummaryDto>();

            var attachments = attachmentsByMessage.TryGetValue(m.Id, out var rows)
                ? rows.Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CdnUrl)).ToList()
                : new List<AttachmentDto>();

            return new MessageDto(
                m.Id,
                m.AuthorId,
                m.Content,
                m.ParentMessageId,
                preview,
                m.CreatedAt,
                m.EditedAt,
                m.IsDeleted,
                reactions,
                attachments,
                Convert.ToBase64String(m.Version));
        }).ToList();

        return new GetChannelMessagesResponse(dtos, nextCursor);
    }
}
