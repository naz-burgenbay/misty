using FluentValidation;
using MediatR;

namespace Misty.Application.Users;

public record SearchUsersQuery(string Query, Guid? ExcludeUserId, int Take) : IRequest<SearchUsersResponse>;

public record SearchUsersResponse(IReadOnlyList<UserSearchMatch> Results);

public record UserSearchMatch(Guid UserId, string Username, string DisplayName, string? AvatarUrl);

public sealed class SearchUsersQueryValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersQueryValidator()
    {
        RuleFor(x => x.Query).NotNull().MaximumLength(100);
    }
}

public sealed class SearchUsersQueryHandler : IRequestHandler<SearchUsersQuery, SearchUsersResponse>
{
    private readonly IUserRepository _users;

    public SearchUsersQueryHandler(IUserRepository users) => _users = users;

    public async Task<SearchUsersResponse> Handle(SearchUsersQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 20);
        var users = await _users.SearchByUsernameAsync(request.Query ?? string.Empty, request.ExcludeUserId, take, ct);
        var matches = users
            .Select(u => new UserSearchMatch(u.Id, u.Username, u.DisplayName, u.AvatarUrl))
            .ToList();
        return new SearchUsersResponse(matches);
    }
}
