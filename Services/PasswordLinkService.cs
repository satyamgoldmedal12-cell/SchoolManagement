using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public class PasswordLinkService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<ApplicationSettings> _settings;

    public PasswordLinkService(
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        IOptions<ApplicationSettings> applicationSettings)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _settings = applicationSettings;
    }

    public async Task SendAsync(ApplicationUser user, bool invitation)
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

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var encodedToken = WebEncoders.Base64UrlEncode(tokenBytes);
        var queryParameters = new Dictionary<string, string?>
        {
            ["userId"] = user.Id,
            ["code"] = encodedToken
        };

        var resetUrl = QueryHelpers.AddQueryString(baseUrl + "/Account/ResetPassword", queryParameters);
        await _emailSender.SendPasswordLinkAsync(user.Email!, user.FullName, resetUrl, invitation);
    }
}
