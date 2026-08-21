using System.Net;
using System.Net.Mail;
using CampusTrack.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusTrack.Infrastructure.Notifications;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@campustrack.local";
    public string FromName { get; set; } = "CampusTrack";
}

/// <summary>
/// Sends mail over SMTP. Reports itself unconfigured when no host is set, so email simply
/// does not happen rather than throwing on every notification - a school without SMTP should
/// still have a fully working product.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!IsConfigured)
            _logger.LogInformation("SMTP is not configured; email delivery is disabled.");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(_options.UserName))
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            // Never surfaced to the caller as a failure of their action: a grade was still
            // saved even if the courtesy email did not go out.
            _logger.LogError(ex, "Could not send email to {Recipient}", to);
            return false;
        }
    }
}
