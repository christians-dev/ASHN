using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Options;

namespace Ashen.BuildingBlocks.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>Sends transactional email. Swappable for SES or another provider later (constitution III).</summary>
public interface IEmailSender
{
    Task Send(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    [Required]
    public string Host { get; set; } = "";

    [Range(1, 65535)]
    public int Port { get; set; } = 25;

    public bool EnableSsl { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }

    [Required, EmailAddress]
    public string FromAddress { get; set; } = "";

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}

// shortcut: System.Net.Mail SmtpClient, switch to SES/MailKit for production
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> smtpOptions, IOptions<StoreOptions> storeOptions) : IEmailSender
{
    public async Task Send(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var smtp = smtpOptions.Value;

        using var mail = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, storeOptions.Value.EmailSenderName),
            Subject = message.Subject,
            Body = message.TextBody,
        };
        mail.To.Add(message.To);

        if (message.HtmlBody is not null)
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));
        }

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            Timeout = smtp.TimeoutSeconds * 1000, // constitution VIII: external calls have timeouts
        };

        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
        }

        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>Fills <c>{{name}}</c> placeholders; values are HTML-encoded for HTML templates.</summary>
public static class EmailTemplate
{
    public static string Render(string template, IReadOnlyDictionary<string, string> values, bool html = false) =>
        values.Aggregate(template, (text, pair) =>
            text.Replace("{{" + pair.Key + "}}", html ? WebUtility.HtmlEncode(pair.Value) : pair.Value, StringComparison.Ordinal));
}
