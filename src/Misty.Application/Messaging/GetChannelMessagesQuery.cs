using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Messaging;

public record GetChannelMessagesQuery(
    Guid ChannelId,
    Guid UserId,
    int PageSize,
    string? Cursor)
    : IRequest<GetChannelMessagesResponse>;

public sealed class GetChannelMessagesQueryValidator : AbstractValidator<GetChannelMessagesQuery>
{
    public GetChannelMessagesQueryValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        // PageSize is clamped by handler, so no validation needed
    }
}

public record GetChannelMessagesResponse(
    List<MessageDto> Messages,
    string? NextCursor);

public record MessageDto(
    Guid Id,
    Guid AuthorId,
    string Content,
    Guid? ParentMessageId,
    ParentPreviewDto? ParentPreview,
    DateTime CreatedAt,
    DateTime? EditedAt,
    bool IsDeleted,
    IReadOnlyList<ReactionSummaryDto> Reactions,
    IReadOnlyList<AttachmentDto> Attachments,
    string Version);

public record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string CdnUrl);

public record ParentPreviewDto(
    Guid Id,
    Guid AuthorId,
    string Content,
    bool IsDeleted);

public record ReactionSummaryDto(
    string EmojiCode,
    int Count,
    bool ReactedByMe);

public sealed class GetChannelMessagesQueryHandler
    : IRequestHandler<GetChannelMessagesQuery, GetChannelMessagesResponse>
{
    private readonly IMessageRepository _messages;
    private readonly IReactionRepository _reactions;
    private readonly IAttachmentRepository _attachments;
    private readonly IPermissionService _permissions;

    public GetChannelMessagesQueryHandler(
        IMessageRepository messages,
        IReactionRepository reactions,
        IAttachmentRepository attachments,
        IPermissionService permissions)
    {
        _messages = messages;
        _reactions = reactions;
        _attachments = attachments;
        _permissions = permissions;
    }

    public async Task<GetChannelMessagesResponse> Handle(GetChannelMessagesQuery request, CancellationToken ct)
    {
        var canRead = await _permissions.CheckPermissionAsync(
            request.UserId,
            request.ChannelId,
            ChannelPermission.ReadHistory,
            ct);

        if (!canRead)
            throw new ForbiddenException("You do not have permission to read messages in this channel.");

        var (messages, nextCursor) = await _messages.GetByChannelAsync(
            request.ChannelId,
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
            {
                // For tombstones, Content is already empty and IsDeleted is true. The preview reflects that directly.
                preview = new ParentPreviewDto(parent.Id, parent.AuthorId, parent.Content, parent.IsDeleted);
            }

            var reactions = reactionsByMessage.TryGetValue(m.Id, out var aggs)
                ? aggs
                    .Select(a => new ReactionSummaryDto(a.EmojiCode, a.Count, a.ReactedByMe))
                    .ToList()
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
