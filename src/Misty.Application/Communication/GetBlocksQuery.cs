using MediatR;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Communication;

public record GetBlocksQuery(Guid UserId) : IRequest<IReadOnlyList<BlockedUserDto>>;

public sealed class GetBlocksQueryHandler
    : IRequestHandler<GetBlocksQuery, IReadOnlyList<BlockedUserDto>>
{
    private readonly IUserBlockService _blocks;

    public GetBlocksQueryHandler(IUserBlockService blocks) => _blocks = blocks;

    public Task<IReadOnlyList<BlockedUserDto>> Handle(GetBlocksQuery query, CancellationToken ct)
        => _blocks.GetBlocksAsync(query.UserId, ct);
}
