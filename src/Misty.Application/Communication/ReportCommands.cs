using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record SubmitReportCommand(
    Guid ReporterId,
    ReportTargetKind TargetKind,
    Guid TargetId,
    string Reason) : IRequest<Guid>;

public sealed class SubmitReportCommandHandler : IRequestHandler<SubmitReportCommand, Guid>
{
    private readonly IReportRepository _reports;

    public SubmitReportCommandHandler(IReportRepository reports) => _reports = reports;

    public async Task<Guid> Handle(SubmitReportCommand cmd, CancellationToken ct)
    {
        var report = Report.Create(Guid.NewGuid(), cmd.ReporterId, cmd.TargetKind, cmd.TargetId, cmd.Reason.Trim());
        await _reports.AddAsync(report, ct);
        return report.Id;
    }
}

public record ReviewReportCommand(Guid AdminId, Guid ReportId, bool Approve) : IRequest;

public sealed class ReviewReportCommandHandler : IRequestHandler<ReviewReportCommand>
{
    private readonly IReportRepository _reports;
    private readonly IChannelRepository _channels;
    private readonly IInboxItemRepository _inbox;
    private readonly Misty.Application.Users.IUserRepository _users;
    private readonly Misty.Application.Users.IEmailService _email;

    public ReviewReportCommandHandler(
        IReportRepository reports,
        IChannelRepository channels,
        IInboxItemRepository inbox,
        Misty.Application.Users.IUserRepository users,
        Misty.Application.Users.IEmailService email)
    {
        _reports = reports;
        _channels = channels;
        _inbox = inbox;
        _users = users;
        _email = email;
    }

    public async Task Handle(ReviewReportCommand cmd, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(cmd.ReportId, ct)
            ?? throw new NotFoundException("Report not found.");

        if (report.Status != ReportStatus.Pending)
            throw new ConflictException("Report has already been reviewed.");

        if (cmd.Approve)
        {
            report.Approve(cmd.AdminId);

            if (report.TargetKind == ReportTargetKind.User)
            {
                var user = await _users.GetByIdAsync(report.TargetId, ct);
                if (user is not null && !user.IsDeleted)
                {
                    await _users.SoftDeleteAsync(user, ct);

                    try
                    {
                        await _email.SendAsync(
                            user.Email,
                            "Your Misty account has been suspended",
                            "<p>Your account on Misty has been suspended following a review of a report against you.</p>" +
                            "<p>If you believe this was a mistake, please contact support.</p>",
                            ct);
                    }
                    catch { }

                    await _inbox.AddAsync(InboxItem.Create(
                        Guid.NewGuid(), report.TargetId,
                        InboxItemType.ReportApproved, cmd.AdminId, report.Id), ct);
                }
            }
            else 
            {
                var channel = await _channels.GetByIdAsync(report.TargetId, ct);
                if (channel is not null && !channel.IsDeleted)
                {
                    await _channels.SoftDeleteAsync(channel, ct);

                    await _inbox.AddAsync(InboxItem.Create(
                        Guid.NewGuid(), channel.CreatedByUserId,
                        InboxItemType.ChannelDeletedByAdmin, cmd.AdminId, report.TargetId), ct);
                }
            }
        }
        else
        {
            report.Disapprove(cmd.AdminId);

            await _inbox.AddAsync(InboxItem.Create(
                Guid.NewGuid(), report.ReporterId,
                InboxItemType.ReportDisapproved, cmd.AdminId, report.Id), ct);
        }

        await _reports.SaveChangesAsync(ct);
    }
}
