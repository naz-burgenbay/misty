using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Presence;

namespace Misty.Api.Realtime;

[ApiController]
[Route("api/v1/presence")]
[Authorize]
public sealed class PresenceController : ControllerBase
{
    private readonly IPresenceTracker _presence;

    public PresenceController(IPresenceTracker presence) => _presence = presence;

    [HttpPost("bulk")]
    [ProducesResponseType(typeof(BulkPresenceResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BulkPresenceResponse>> Bulk(BulkPresenceRequest request, CancellationToken ct)
    {
        var ids = (request.UserIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var statuses = await _presence.GetOnlineStatusAsync(ids, ct);
        return Ok(new BulkPresenceResponse(
            statuses.Select(kv => new PresenceStatusDto(kv.Key, kv.Value)).ToList()));
    }
}

public sealed record BulkPresenceRequest(IReadOnlyList<Guid>? UserIds);
public sealed record BulkPresenceResponse(IReadOnlyList<PresenceStatusDto> Statuses);
public sealed record PresenceStatusDto(Guid UserId, bool IsOnline);
