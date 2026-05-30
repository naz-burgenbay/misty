using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record ApplyModerationActionCommand(
    Guid ChannelId,
    Guid TargetUserId,
    Guid IssuedByUserId,
    ModerationActionType Type,
    string Reason,
    DateTime? ExpiresAt)
    : IRequest<ApplyModerationActionResponse>;

public record ApplyModerationActionResponse(Guid ActionId);

public sealed class ApplyModerationActionCommandHandler
    : IRequestHandler<ApplyModerationActionCommand, ApplyModerationActionResponse>
{
    private readonly IModerationRepository _moderation;
    private readonly IChannelRepository _channels;
    private readonly IPermissionService _permissions;
    private readonly IEventPublisher _events;

    public ApplyModerationActionCommandHandler(
        IModerationRepository moderation,
        IChannelRepository channels,
        IPermissionService permissions,
        IEventPublisher events)
    {
        _moderation = moderation;
        _channels = channels;
        _permissions = permissions;
        _events = events;
    }

    public async Task<ApplyModerationActionResponse> Handle(
        ApplyModerationActionCommand request, CancellationToken ct)
    {
        if (request.IssuedByUserId == request.TargetUserId)
            throw new ForbiddenException("Cannot moderate yourself.");

        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        if (channel.CreatedByUserId == request.TargetUserId)
            throw new ForbiddenException("Cannot moderate the channel owner.");

        var required = RequiredPermissionFor(request.Type);
        var allowed = await _permissions.CheckPermissionAsync(
            request.IssuedByUserId, request.ChannelId, required, ct);
        if (!allowed)
            throw new ForbiddenException($"Missing {required} permission.");

        var alreadyActive = await _moderation.HasActiveAsync(
            request.ChannelId, request.TargetUserId, request.Type, ct);

        if (alreadyActive)
            throw new ConflictException(
                $"User already has an active {request.Type} action in this channel.");

        var action = ModerationAction.Create(
            Guid.NewGuid(),
            request.ChannelId,
            request.TargetUserId,
            request.IssuedByUserId,
            request.Type,
            request.Reason.Trim(),
            request.ExpiresAt);

        await _moderation.AddAsync(action, ct);
        await _events.PublishModerationActionAppliedAsync(request.TargetUserId, request.ChannelId, ct);

        return new ApplyModerationActionResponse(action.Id);
    }

    internal static ChannelPermission RequiredPermissionFor(ModerationActionType type) => type switch
    {
        ModerationActionType.Mute => ChannelPermission.MuteMembers,
        ModerationActionType.Ban  => ChannelPermission.BanMembers,
        ModerationActionType.Warn => ChannelPermission.MuteMembers,
        _ => throw new InvalidOperationException($"Unsupported moderation type '{type}'."),
    };
}

public sealed class ApplyModerationActionValidator : AbstractValidator<ApplyModerationActionCommand>
{
    public ApplyModerationActionValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.IssuedByUserId).NotEmpty();

        RuleFor(x => x.Type)
            .NotEqual(ModerationActionType.Kick)
            .WithMessage("Use DELETE /api/v1/channels/{channelId}/members/{userId} to kick a member.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(512);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.ExpiresAt.HasValue)
            .WithMessage("ExpiresAt must be in the future.");
    }
}

public record RevokeModerationActionCommand(Guid ChannelId, Guid RevokedByUserId, Guid ActionId) : IRequest;

public sealed class RevokeModerationActionCommandHandler : IRequestHandler<RevokeModerationActionCommand>
{
    private readonly IModerationRepository _moderation;
    private readonly IPermissionService _permissions;
    private readonly IEventPublisher _events;

    public RevokeModerationActionCommandHandler(
        IModerationRepository moderation,
        IPermissionService permissions,
        IEventPublisher events)
    {
        _moderation = moderation;
        _permissions = permissions;
        _events = events;
    }

    public async Task Handle(RevokeModerationActionCommand request, CancellationToken ct)
    {
        var action = await _moderation.GetByIdAsync(request.ActionId, ct);

        if (action is null || action.ChannelId != request.ChannelId)
            throw new NotFoundException($"Moderation action '{request.ActionId}' was not found.");

        if (action.Type == ModerationActionType.Kick)
            throw new ConflictException("Kick actions are historical and cannot be revoked.");

        if (action.RevokedAt is not null)
            throw new ConflictException("Moderation action has already been revoked.");

        var required = ApplyModerationActionCommandHandler.RequiredPermissionFor(action.Type);
        var allowed = await _permissions.CheckPermissionAsync(
            request.RevokedByUserId, request.ChannelId, required, ct);
        if (!allowed)
            throw new ForbiddenException($"Missing {required} permission.");

        action.Revoke(DateTime.UtcNow);
        await _moderation.UpdateAsync(action, ct);
        await _events.PublishModerationActionAppliedAsync(action.TargetUserId, action.ChannelId, ct);
    }
}
