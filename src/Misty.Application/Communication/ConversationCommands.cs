using FluentValidation;
using MediatR;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record CreateConversationCommand(Guid RequestingUserId, Guid OtherUserId)
    : IRequest<CreateConversationResponse>;

public record CreateConversationResponse(Guid ConversationId);

public sealed class CreateConversationValidator : AbstractValidator<CreateConversationCommand>
{
    public CreateConversationValidator()
    {
        RuleFor(x => x.OtherUserId)
            .NotEqual(x => x.RequestingUserId)
            .WithMessage("Cannot create a conversation with yourself.");
    }
}

public sealed class CreateConversationHandler : IRequestHandler<CreateConversationCommand, CreateConversationResponse>
{
    private readonly IConversationRepository _repo;

    public CreateConversationHandler(IConversationRepository repo) => _repo = repo;

    public async Task<CreateConversationResponse> Handle(
        CreateConversationCommand request, CancellationToken cancellationToken)
    {
        // Canonicalise ordering so (A,B) and (B,A) hit the same DB row
        var (a, b) = request.RequestingUserId.CompareTo(request.OtherUserId) < 0
            ? (request.RequestingUserId, request.OtherUserId)
            : (request.OtherUserId, request.RequestingUserId);

        var existing = await _repo.GetByUsersAsync(a, b, cancellationToken);
        if (existing is not null)
            return new(existing.Id);

        var conversation = Conversation.Create(Guid.NewGuid(), request.RequestingUserId, request.OtherUserId);
        await _repo.AddAsync(conversation, cancellationToken);
        return new(conversation.Id);
    }
}

public record GetConversationsQuery(Guid UserId) : IRequest<IReadOnlyList<ConversationDto>>;

public record ConversationDto(Guid ConversationId, Guid OtherUserId, string Version);

public sealed class GetConversationsQueryValidator : AbstractValidator<GetConversationsQuery>
{
    public GetConversationsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class GetConversationsHandler : IRequestHandler<GetConversationsQuery, IReadOnlyList<ConversationDto>>
{
    private readonly IConversationRepository _repo;

    public GetConversationsHandler(IConversationRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ConversationDto>> Handle(
        GetConversationsQuery request, CancellationToken cancellationToken)
    {
        var conversations = await _repo.GetForUserAsync(request.UserId, cancellationToken);
        return conversations
            .Select(c => new ConversationDto(
                c.Id,
                c.UserAId == request.UserId ? c.UserBId : c.UserAId,
                Convert.ToBase64String(c.Version)))
            .ToList();
    }
}
