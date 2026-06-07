using FluentValidation;
using MediatR;
using Misty.Application.Users;

namespace Misty.Application.Users;

public record GetUserByIdQuery(Guid UserId) : IRequest<GetUserByIdResponse?>;

public record GetUserByIdResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string Version);

public sealed class GetUserByIdQueryValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, GetUserByIdResponse?>
{
    private readonly IUserRepository _users;

    public GetUserByIdQueryHandler(IUserRepository users) => _users = users;

    public async Task<GetUserByIdResponse?> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await _users.GetByIdForReadAsync(request.UserId, ct);
        if (user is null)
            return null;

        return new GetUserByIdResponse(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            Convert.ToBase64String(user.Version));
    }
}
