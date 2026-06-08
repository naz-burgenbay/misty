using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public record UploadMessageAttachmentCommand(
    Guid UserId,
    Guid ChannelId,
    Guid MessageId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content)
    : IRequest<UploadMessageAttachmentResponse>;

public record UploadMessageAttachmentResponse(
    Guid AttachmentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string CdnUrl);

public sealed class UploadMessageAttachmentCommandHandler
    : IRequestHandler<UploadMessageAttachmentCommand, UploadMessageAttachmentResponse>
{
    public const string MessageContainer = "attachments";

    private readonly IMessageRepository _messages;
    private readonly IAttachmentRepository _attachments;
    private readonly IAttachmentStorage _storage;
    private readonly IPermissionService _permissions;

    public UploadMessageAttachmentCommandHandler(
        IMessageRepository messages,
        IAttachmentRepository attachments,
        IAttachmentStorage storage,
        IPermissionService permissions)
    {
        _messages = messages;
        _attachments = attachments;
        _storage = storage;
        _permissions = permissions;
    }

    public async Task<UploadMessageAttachmentResponse> Handle(
        UploadMessageAttachmentCommand request,
        CancellationToken ct)
    {
        var canAttach = await _permissions.CheckPermissionAsync(
            request.UserId,
            request.ChannelId,
            ChannelPermission.AttachFiles,
            ct);

        if (!canAttach)
            throw new ForbiddenException("You do not have permission to attach files in this channel.");

        var message = await _messages.GetByIdAsync(request.MessageId, ct);
        if (message is null || message.ChannelId != request.ChannelId)
            throw new NotFoundException("Message not found.");
        if (message.AuthorId != request.UserId)
            throw new ForbiddenException("You can only attach files to your own messages.");
        if (message.IsDeleted)
            throw new ValidationException("Cannot attach files to a deleted message.");

        var attachmentId = Guid.NewGuid();
        var blobName = $"{request.MessageId}/{attachmentId}";

        var cdnUrl = await _storage.UploadAsync(
            MessageContainer,
            blobName,
            request.Content,
            request.ContentType,
            ct);

        var attachment = Attachment.CreateForMessage(
            attachmentId,
            request.MessageId,
            MessageContainer,
            blobName,
            request.FileName,
            request.ContentType,
            request.SizeBytes,
            cdnUrl);

        await _attachments.AddAsync(attachment, ct);

        return new UploadMessageAttachmentResponse(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.CdnUrl);
    }
}

public sealed class UploadMessageAttachmentCommandValidator : AbstractValidator<UploadMessageAttachmentCommand>
{
    public const long MaxSizeBytes = 25L * 1024 * 1024;

    public UploadMessageAttachmentCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(128);
        RuleFor(x => x.SizeBytes).GreaterThan(0).LessThanOrEqualTo(MaxSizeBytes);
    }
}
