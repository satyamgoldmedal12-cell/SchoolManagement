using System.Text.Encodings.Web;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IOptions<SmtpSettings> _settings;

    public SmtpEmailSender(IOptions<SmtpSettings> settings)
    {
        _settings = settings;
    }

    public async Task SendTemporaryCredentialsAsync(string email, string name, string password, string loginUrl, DateTime expiresAt)
    {
        var message = CreateMessage(email, name);
        message.Subject = "Your School Management login";

        var encodedName = HtmlEncoder.Default.Encode(name);
        var encodedEmail = HtmlEncoder.Default.Encode(email);
        var encodedPassword = HtmlEncoder.Default.Encode(password);
        var encodedLoginUrl = HtmlEncoder.Default.Encode(loginUrl);

        var body = new BodyBuilder
        {
            TextBody = $"Hello {name},\n\nYour school account has been created.\nLogin: {loginUrl}\nEmail: {email}\nTemporary password: {password}\n\nYou must change this password on first login. It expires at {expiresAt:yyyy-MM-dd HH:mm} UTC. Do not forward this email.",
            HtmlBody = $"<p>Hello {encodedName},</p><p>Your school account has been created.</p><p><a href=\"{encodedLoginUrl}\">Log in to School Management</a></p><p>Email: {encodedEmail}<br />Temporary password: <strong>{encodedPassword}</strong></p><p>You must change this password on first login. It expires at {expiresAt:yyyy-MM-dd HH:mm} UTC. Do not forward this email.</p>"
        };

        message.Body = body.ToMessageBody();
        await SendMessageAsync(message);
    }

    public async Task SendPasswordLinkAsync(string email, string name, string url, bool invitation)
    {
        var message = CreateMessage(email, name);

        string action;
        if (invitation)
        {
            action = "Set your password";
        }
        else
        {
            action = "Reset your password";
        }

        message.Subject = $"School Management — {action}";

        var encodedName = HtmlEncoder.Default.Encode(name);
        var encodedUrl = HtmlEncoder.Default.Encode(url);
        var body = new BodyBuilder
        {
            TextBody = $"Hello {name},\n\n{action} using this link:\n{url}\n\nThis link expires in 60 minutes and can be used once. If you did not request this, ignore this message.",
            HtmlBody = $"<p>Hello {encodedName},</p><p><a href=\"{encodedUrl}\">{action}</a></p><p>This link expires in 60 minutes and can be used once. If you did not request this, ignore this message.</p>"
        };

        message.Body = body.ToMessageBody();
        await SendMessageAsync(message);
    }

    private MimeMessage CreateMessage(string email, string name)
    {
        var smtp = _settings.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromEmail))
        {
            throw new InvalidOperationException("SMTP host and sender email must be configured.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtp.FromName, smtp.FromEmail));
        message.To.Add(new MailboxAddress(name, email));
        return message;
    }

    private async Task SendMessageAsync(MimeMessage message)
    {
        var smtp = _settings.Value;
        using var client = new SmtpClient { Timeout = 15000 };

        SecureSocketOptions connectionOptions;
        if (smtp.UseSsl)
        {
            connectionOptions = SecureSocketOptions.SslOnConnect;
        }
        else
        {
            connectionOptions = SecureSocketOptions.StartTls;
        }

        await client.ConnectAsync(smtp.Host, smtp.Port, connectionOptions);

        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password);
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
