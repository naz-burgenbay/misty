using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Users;

public record UpdateUserCommand(Guid UserId, string DisplayName, string? Bio, string Version)
    : IRequest<UpdateUserResponse>;

public record UpdateUserResponse(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string Version);

public sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UpdateUserResponse>
{
    private readonly IUserRepository _users;
    private readonly IOutboxWriter _outbox;

    public UpdateUserCommandHandler(IUserRepository users, IOutboxWriter outbox)
    {
        _users = users;
        _outbox = outbox;
    }

    public async Task<UpdateUserResponse> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct)
            ?? throw new UnauthorizedException();

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(request.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        user.UpdateProfile(request.DisplayName, request.Bio);
        await _users.UpdateAsync(user, concurrencyToken, ct);
        await _outbox.WriteAsync(
            UserEventTopics.User,
            UserEventTypes.UserProfileUpdated,
            user.Id,
            new UserProfileUpdatedPayload(user.Id, user.DisplayName, user.Bio, DateTime.UtcNow),
            ct);

        return new UpdateUserResponse(
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            Convert.ToBase64String(user.Version));
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Bio).MaximumLength(500).When(x => x.Bio is not null);
        RuleFor(x => x.Version).NotEmpty();
    }
}
