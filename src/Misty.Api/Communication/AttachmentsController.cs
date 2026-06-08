using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Messaging;

namespace Misty.Api.Communication;

[ApiController]
[Authorize]
public sealed class AttachmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AttachmentsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("api/v1/channels/{channelId:guid}/messages/{messageId:guid}/attachments")]
    [ProducesResponseType(typeof(UploadMessageAttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> UploadMessageAttachment(
        Guid channelId,
        Guid messageId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ValidationProblem("File is required.");

        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(
            new UploadMessageAttachmentCommand(
                userId,
                channelId,
                messageId,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                file.Length,
                stream),
            ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpDelete("api/v1/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAttachment(Guid attachmentId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DeleteAttachmentCommand(userId, attachmentId), ct);
        return NoContent();
    }
}
