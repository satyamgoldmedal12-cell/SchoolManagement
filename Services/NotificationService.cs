using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class NotificationService : INotificationService
{
    private readonly IEmailSender _emailSender;
    private readonly IOptions<ApplicationSettings> _settings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PasswordLinkService _passwordLinks;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IEmailSender emailSender,
        IOptions<ApplicationSettings> settings,
        UserManager<ApplicationUser> userManager,
        PasswordLinkService passwordLinks,
        ILogger<NotificationService> logger)
    {
        _emailSender = emailSender;
        _settings = settings;
        _userManager = userManager;
        _passwordLinks = passwordLinks;
        _logger = logger;
    }

    public string LoginUrl
    {
        get
        {
            var baseUrl = _settings.Value.PublicBaseUrl.TrimEnd('/');
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address))
            {
                throw new InvalidOperationException("Configure Application:PublicBaseUrl as the application's HTTPS address.");
            }

            if (address.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(address.UserInfo) ||
                !string.IsNullOrEmpty(address.Query) ||
                !string.IsNullOrEmpty(address.Fragment))
            {
                throw new InvalidOperationException("Configure Application:PublicBaseUrl as the application's HTTPS address.");
            }

            return baseUrl + "/Account/Login";
        }
    }

    public async Task<bool> SendTemporaryPasswordAsync(UserAccess user, string password, DateTime expiresAt)
    {
        try
        {
            await _emailSender.SendTemporaryCredentialsAsync(user.Email, user.FullName, password, LoginUrl, expiresAt);
            return true;
        }
        catch (Exception exception)
        {
            LogFailure(user.Id, exception);
            return false;
        }
    }

    public async Task<bool> SendResetLinkAsync(UserAccess user)
    {
        try
        {
            var account = await _userManager.FindByIdAsync(user.Id);
            if (account is null)
            {
                return false;
            }

            await _passwordLinks.SendAsync(account, invitation: false);
            return true;
        }
        catch (Exception exception)
        {
            LogFailure(user.Id, exception);
            return false;
        }
    }

    private void LogFailure(string userId, Exception exception)
    {
        _logger.LogWarning(
            "Account email failed for user {UserId}: {FailureType}",
            userId, exception.GetType().Name);
    }
}
