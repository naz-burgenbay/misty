using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
namespace Misty.Application.Communication;

public record CreateChannelCommand(
    Guid UserId,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions,
    string? Description = null)
    : IRequest<CreateChannelResponse>;

public record CreateChannelResponse(
    Guid ChannelId,
    string Name,
    bool IsPrivate,
    string? InviteCode,
    bool IsAiAssistantEnabled,
    long DefaultPermissions,
    string Version);

public sealed class CreateChannelCommandHandler : IRequestHandler<CreateChannelCommand, CreateChannelResponse>
{
    private readonly IChannelRepository _channels;
    private readonly IOutboxWriter _outbox;

    public CreateChannelCommandHandler(IChannelRepository channels, IOutboxWriter outbox)
    {
        _channels = channels;
        _outbox = outbox;
    }

    public async Task<CreateChannelResponse> Handle(CreateChannelCommand request, CancellationToken ct)
    {
        var channel = Channel.Create(
            Guid.NewGuid(),
            request.Name,
            request.IsPrivate,
            request.IsAiAssistantEnabled,
            request.DefaultPermissions,
            request.UserId,
            request.Description);

        var ownerRole = ChannelRole.CreateOwner(Guid.NewGuid(), channel.Id);
        var creatorMembership = Membership.Create(Guid.NewGuid(), channel.Id, request.UserId);
        channel.IncrementMemberCount();
        var ownerMemberRole = MemberRole.Create(creatorMembership.Id, ownerRole.Id);

        _outbox.Queue(
            ChannelEventTopics.Channel,
            ChannelEventTypes.ChannelCreated,
            channel.Id,
            new ChannelCreatedPayload(
                channel.Id,
                request.UserId,
                channel.Name,
                channel.IsPrivate,
                DateTime.UtcNow));

        await _channels.CreateWithOwnerAsync(channel, ownerRole, creatorMembership, ownerMemberRole, ct);

        return new CreateChannelResponse(
            channel.Id,
            channel.Name,
            channel.IsPrivate,
            channel.InviteCode,
            channel.IsAiAssistantEnabled,
            (long)channel.DefaultPermissions,
            Convert.ToBase64String(channel.Version));
    }
}

public sealed class CreateChannelValidator : AbstractValidator<CreateChannelCommand>
{
    public CreateChannelValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
