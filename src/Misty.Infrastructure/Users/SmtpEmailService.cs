using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Misty.Application.Users;

namespace Misty.Infrastructure.Users;

public sealed class SmtpEmailService : IEmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _from;
    private readonly string? _username;
    private readonly string? _password;

    public SmtpEmailService(IConfiguration configuration)
    {
        var section = configuration.GetSection("Email");
        _host     = section["SmtpHost"]  ?? "localhost";
        _port     = int.TryParse(section["SmtpPort"], out var p) ? p : 25;
        _from     = section["From"]      ?? "noreply@misty.app";
        _username = section["Username"];
        _password = section["Password"];
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_host, _port);
        if (!string.IsNullOrEmpty(_username))
        {
            client.Credentials = new NetworkCredential(_username, _password);
            client.EnableSsl = true;
        }

        using var message = new MailMessage(_from, to, subject, htmlBody) { IsBodyHtml = true };
        await client.SendMailAsync(message, ct);
    }
}
