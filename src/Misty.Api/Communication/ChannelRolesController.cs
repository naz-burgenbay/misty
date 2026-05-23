using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using Misty.Domain.Communication;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels/{channelId:guid}/roles")]
[Authorize]
public sealed class ChannelRolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChannelRolesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(List<ChannelRoleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRoles(Guid channelId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetChannelRolesQuery(channelId), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ChannelRoleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateRole(Guid channelId, CreateRoleRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateChannelRoleCommand(channelId, request.Name, request.Permissions), ct);
        return CreatedAtAction(nameof(GetRoles), new { channelId }, result);
    }

    [HttpPut("{roleId:guid}")]
    [ProducesResponseType(typeof(ChannelRoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateRole(Guid channelId, Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateChannelRoleCommand(channelId, roleId, request.Name, request.Permissions), ct);
        return Ok(result);
    }

    [HttpDelete("{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRole(Guid channelId, Guid roleId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteChannelRoleCommand(channelId, roleId), ct);
        return NoContent();
    }
}

public record CreateRoleRequest(string Name, ChannelPermission Permissions);
public record UpdateRoleRequest(string Name, ChannelPermission Permissions);
