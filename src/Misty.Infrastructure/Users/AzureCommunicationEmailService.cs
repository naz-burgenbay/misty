using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Misty.Application.Users;

namespace Misty.Infrastructure.Users;

public sealed class AzureCommunicationEmailService : IEmailService
{
    private readonly EmailClient _client;
    private readonly string _senderAddress;
    private readonly ILogger<AzureCommunicationEmailService> _logger;

    public AzureCommunicationEmailService(IConfiguration configuration, ILogger<AzureCommunicationEmailService> logger)
    {
        var cs = configuration.GetConnectionString("AzureCommunicationEmail")
            ?? throw new InvalidOperationException("Connection string 'AzureCommunicationEmail' is not configured.");
        _senderAddress = configuration["Email:From"]
            ?? throw new InvalidOperationException("'Email:From' is not configured.");
        _client = new EmailClient(cs);
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new EmailMessage(
            senderAddress: _senderAddress,
            content: new EmailContent(subject) { Html = htmlBody },
            recipients: new EmailRecipients([new EmailAddress(to)]));

        // Fire-and-forget
        _ = Task.Run(async () =>
        {
            try
            {
                var operation = await _client.SendAsync(WaitUntil.Started, message);
                _logger.LogInformation("ACS email queued to {To}, operationId={Id}.", to, operation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to queue ACS email to {To}.", to);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
