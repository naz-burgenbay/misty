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

public record GetChannelMessagesResponse(
    List<MessageDto> Messages,
    string? NextCursor);

public record MessageDto(
    Guid Id,
    Guid AuthorId,
    string Content,
    Guid? ParentMessageId,
    DateTime CreatedAt,
    DateTime? EditedAt,
    bool IsDeleted);

public sealed class GetChannelMessagesQueryHandler
    : IRequestHandler<GetChannelMessagesQuery, GetChannelMessagesResponse>
{
    private readonly IMessageRepository _messages;
    private readonly IPermissionService _permissions;

    public GetChannelMessagesQueryHandler(
        IMessageRepository messages,
        IPermissionService permissions)
    {
        _messages = messages;
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

        var dtos = messages.Select(m => new MessageDto(
            m.Id,
            m.AuthorId,
            m.Content,
            m.ParentMessageId,
            m.CreatedAt,
            m.EditedAt,
            m.IsDeleted)).ToList();

        return new GetChannelMessagesResponse(dtos, nextCursor);
    }
}
