using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using Misty.Domain.Communication;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/reports")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Submit(SubmitReportRequest request, CancellationToken ct)
    {
        var reporterId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        if (!Enum.TryParse<ReportTargetKind>(request.TargetKind, ignoreCase: true, out var kind))
            return BadRequest(new { error = "Invalid target kind." });

        var reportId = await _mediator.Send(
            new SubmitReportCommand(reporterId, kind, request.TargetId, request.Reason), ct);

        return Created($"/api/v1/reports/{reportId}", new { reportId });
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPending([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (!IsAdmin()) return Forbid();
        var reports = await _mediator.Send(new GetReportsQuery(skip, take), ct);
        return Ok(reports);
    }

    [HttpPost("{id:guid}/review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review(Guid id, ReviewReportRequest request, CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();
        var adminId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new ReviewReportCommand(adminId, id, request.Approve), ct);
        return NoContent();
    }

    private bool IsAdmin()
        => User.FindFirst(ClaimTypes.Role)?.Value == "admin";
}

public record SubmitReportRequest(string TargetKind, Guid TargetId, string Reason);
public record ReviewReportRequest(bool Approve);
