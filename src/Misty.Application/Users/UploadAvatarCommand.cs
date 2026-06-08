using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Users;

public record UploadAvatarCommand(Guid UserId, Stream Content, string ContentType, string Version)
    : IRequest<UploadAvatarResponse>;

public record UploadAvatarResponse(string AvatarUrl, string Version);

public sealed class UploadAvatarCommandValidator : AbstractValidator<UploadAvatarCommand>
{
    public UploadAvatarCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class UploadAvatarCommandHandler : IRequestHandler<UploadAvatarCommand, UploadAvatarResponse>
{
    private readonly IAvatarService _avatar;
    private readonly IUserRepository _users;
    private readonly IOutboxWriter _outbox;

    public UploadAvatarCommandHandler(IAvatarService avatar, IUserRepository users, IOutboxWriter outbox)
    {
        _avatar = avatar;
        _users = users;
        _outbox = outbox;
    }

    public async Task<UploadAvatarResponse> Handle(UploadAvatarCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct)
            ?? throw new UnauthorizedException();

        var concurrencyToken = Convert.FromBase64String(request.Version);
        var url = await _avatar.UploadAsync(request.UserId, request.Content, request.ContentType, ct);
        await _users.UpdateAvatarUrlAsync(user, url, concurrencyToken, ct);
        await _outbox.WriteAsync(
            UserEventTopics.User,
            UserEventTypes.UserAvatarChanged,
            user.Id,
            new UserAvatarChangedPayload(user.Id, url, DateTime.UtcNow),
            ct);

        return new UploadAvatarResponse(url, Convert.ToBase64String(user.Version));
    }
}
