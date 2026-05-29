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
    private readonly IEventPublisher _events;

    public ApplyModerationActionCommandHandler(IModerationRepository moderation, IEventPublisher events)
    {
        _moderation = moderation;
        _events = events;
    }

    public async Task<ApplyModerationActionResponse> Handle(
        ApplyModerationActionCommand request, CancellationToken ct)
    {
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
}

public sealed class ApplyModerationActionValidator : AbstractValidator<ApplyModerationActionCommand>
{
    public ApplyModerationActionValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(512);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.ExpiresAt.HasValue)
            .WithMessage("ExpiresAt must be in the future.");
    }
}

public record RevokeModerationActionCommand(Guid ChannelId, Guid ActionId) : IRequest;

public sealed class RevokeModerationActionCommandHandler : IRequestHandler<RevokeModerationActionCommand>
{
    private readonly IModerationRepository _moderation;
    private readonly IEventPublisher _events;

    public RevokeModerationActionCommandHandler(IModerationRepository moderation, IEventPublisher events)
    {
        _moderation = moderation;
        _events = events;
    }

    public async Task Handle(RevokeModerationActionCommand request, CancellationToken ct)
    {
        var action = await _moderation.GetByIdAsync(request.ActionId, ct);

        if (action is null || action.ChannelId != request.ChannelId)
            throw new NotFoundException($"Moderation action '{request.ActionId}' was not found.");

        if (action.RevokedAt is not null)
            throw new ConflictException("Moderation action has already been revoked.");

        action.Revoke(DateTime.UtcNow);
        await _moderation.UpdateAsync(action, ct);
        await _events.PublishModerationActionAppliedAsync(action.TargetUserId, action.ChannelId, ct);
    }
}
