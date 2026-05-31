using MediatR;
using Misty.Application.Common.Exceptions;

namespace Misty.Application.Communication;

public record DismissInboxItemCommand(Guid UserId, Guid ItemId) : IRequest;

public sealed class DismissInboxItemCommandHandler : IRequestHandler<DismissInboxItemCommand>
{
    private readonly IInboxItemRepository _inbox;

    public DismissInboxItemCommandHandler(IInboxItemRepository inbox) => _inbox = inbox;

    public async Task Handle(DismissInboxItemCommand cmd, CancellationToken ct)
    {
        var item = await _inbox.GetByIdAsync(cmd.ItemId, ct)
            ?? throw new NotFoundException("Inbox item not found.");

        if (item.UserId != cmd.UserId)
            throw new ForbiddenException("You do not own this inbox item.");

        if (item.IsActedOn) return;

        item.MarkActedOn();
        await _inbox.UpdateAsync(item, ct);
    }
}
