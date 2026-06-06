using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Users;

public record RemoveAvatarCommand(Guid UserId, string Version) : IRequest<RemoveAvatarResponse>;

public record RemoveAvatarResponse(string Version);

public sealed class RemoveAvatarCommandHandler : IRequestHandler<RemoveAvatarCommand, RemoveAvatarResponse>
{
    private readonly IAvatarService _avatar;
    private readonly IUserRepository _users;
    private readonly IOutboxWriter _outbox;

    public RemoveAvatarCommandHandler(IAvatarService avatar, IUserRepository users, IOutboxWriter outbox)
    {
        _avatar = avatar;
        _users = users;
        _outbox = outbox;
    }

    public async Task<RemoveAvatarResponse> Handle(RemoveAvatarCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct)
            ?? throw new UnauthorizedException();

        var concurrencyToken = Convert.FromBase64String(request.Version);
        await _avatar.DeleteAsync(request.UserId, ct);
        await _users.UpdateAvatarUrlAsync(user, null, concurrencyToken, ct);
        await _outbox.WriteAsync(
            UserEventTopics.User,
            UserEventTypes.UserAvatarChanged,
            user.Id,
            new UserAvatarChangedPayload(user.Id, AvatarUrl: null, DateTime.UtcNow),
            ct);

        return new RemoveAvatarResponse(Convert.ToBase64String(user.Version));
    }
}
