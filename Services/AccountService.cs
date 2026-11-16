using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class AccountService : IAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUserRepository _userRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PasswordLinkService _passwordLinks;
    private readonly ILogger<AccountService> _logger;
    private readonly TimeProvider _clock;
    private readonly ILoginHistoryService _loginHistory;
    private readonly IOptionsMonitor<CookieAuthenticationOptions> _cookieOptions;
    private readonly ILoginMetadataService _loginMetadata;

    public AccountService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IUserRepository userRepository,
        IHttpContextAccessor httpContextAccessor,
        PasswordLinkService passwordLinks,
        ILogger<AccountService> logger,
        TimeProvider clock,
        ILoginHistoryService loginHistory,
        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
        ILoginMetadataService loginMetadata)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _userRepository = userRepository;
        _httpContextAccessor = httpContextAccessor;
        _passwordLinks = passwordLinks;
        _logger = logger;
        _clock = clock;
        _loginHistory = loginHistory;
        _cookieOptions = cookieOptions;
        _loginMetadata = loginMetadata;
    }

    private HttpContext Context
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null)
            {
                throw new InvalidOperationException("An HTTP request is required.");
            }

            return context;
        }
    }

    public async Task<LoginOutcome> LoginAsync(string email, string password, bool rememberMe, LoginDeviceInput? device = null)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return new LoginOutcome(false, Error: "Invalid email or password.");
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                return new LoginOutcome(false, Error: "Too many unsuccessful attempts. Please try again in 15 minutes.");
            }

            return new LoginOutcome(false, Error: "Invalid email or password.");
        }

        if (user.MustChangePassword && IsTemporaryPasswordExpired(user))
        {
            return new LoginOutcome(false, Error: "Your temporary password has expired. Ask your administrator to resend your login email.");
        }

        await StartSessionAsync(user.Id, rememberMe, user.MustChangePassword, device);
        return new LoginOutcome(true, user.MustChangePassword);
    }

    public async Task<OperationResult> ChangeTemporaryPasswordAsync(string password, LoginDeviceInput? device = null)
    {
        var authentication = await Context.AuthenticateAsync(PasswordSetupAuthentication.Scheme);
        var userId = authentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        ApplicationUser? user = null;
        if (userId is not null)
        {
            user = await _userManager.FindByIdAsync(userId);
        }

        const string invalidSessionMessage = "Your password-change session is invalid or expired. Log in with your temporary password again.";

        if (!authentication.Succeeded || user is null || !user.MustChangePassword)
        {
            return OperationResult.Failure(invalidSessionMessage);
        }

        if (!await _signInManager.CanSignInAsync(user) || IsTemporaryPasswordExpired(user))
        {
            return OperationResult.Failure(invalidSessionMessage);
        }

        var sessionSecurityStamp = authentication.Principal!.FindFirstValue(PasswordSetupAuthentication.StampClaim);
        if (user.SecurityStamp != sessionSecurityStamp)
        {
            return OperationResult.Failure(invalidSessionMessage);
        }

        if (await _userManager.CheckPasswordAsync(user, password))
        {
            return OperationResult.Failure("Choose a new password different from your temporary password.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        PreparePasswordChange(user);

        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            return PasswordFailure(result);
        }

        var rememberClaim = authentication.Principal!.FindFirstValue(PasswordSetupAuthentication.RememberClaim);
        bool.TryParse(rememberClaim, out var rememberMe);

        var loginDevice = _loginMetadata.MergePendingDevice(authentication.Principal, device);
        await StartSessionAsync(user.Id, rememberMe, passwordSetupOnly: false, loginDevice);
        return OperationResult.Success(user.Id);
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !await _signInManager.CanSignInAsync(user))
        {
            return;
        }

        try
        {
            await _passwordLinks.SendAsync(user, invitation: false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Password email failed for user {UserId}: {FailureType}",
                user.Id, exception.GetType().Name);
        }
    }

    public async Task<OperationResult> ResetPasswordAsync(string userId, string code, string password)
    {
        var user = await _userManager.FindByIdAsync(userId);

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return InvalidLink();
        }

        if (user is null || !await _signInManager.CanSignInAsync(user))
        {
            return InvalidLink();
        }

        if (await _userManager.CheckPasswordAsync(user, password))
        {
            return OperationResult.Failure("Choose a password different from your current password.");
        }

        PreparePasswordChange(user);
        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            bool hasInvalidToken = result.Errors.Any(error => error.Code == "InvalidToken");
            if (hasInvalidToken)
            {
                return InvalidLink();
            }

            return PasswordFailure(result);
        }

        await _loginHistory.EndUserSessionsAsync(user.Id, LoginHistoryConstants.PasswordReset);
        await SignOutSessionsAsync(LoginHistoryConstants.PasswordReset);
        return OperationResult.Success(user.Id);
    }

    public Task SignOutAsync()
    {
        return SignOutSessionsAsync(LoginHistoryConstants.Logout);
    }

    private async Task SignOutSessionsAsync(string reason)
    {
        await _loginHistory.EndCurrentSessionAsync(Context, reason);
        await _signInManager.SignOutAsync();
        await Context.SignOutAsync(PasswordSetupAuthentication.Scheme);
    }

    private async Task StartSessionAsync(string userId, bool rememberMe, bool passwordSetupOnly, LoginDeviceInput? device)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            throw new InvalidOperationException("User not found.");
        }

        if (passwordSetupOnly)
        {
            await StartPasswordSetupSessionAsync(user, rememberMe, device);
            return;
        }

        if (user.MustChangePassword)
        {
            throw new InvalidOperationException("The first password must be changed before signing in.");
        }

        await Context.SignOutAsync(PasswordSetupAuthentication.Scheme);

        var now = _clock.GetUtcNow();
        var cookieLifetime = _cookieOptions.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan;
        var properties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            IssuedUtc = now,
            ExpiresUtc = now.Add(cookieLifetime)
        };
        var sessionId = await _loginHistory.StartSessionAsync(user, Context, properties, device);
        var sessionClaim = new Claim(LoginHistoryConstants.SessionClaim, sessionId.ToString());

        try
        {
            await _signInManager.SignInWithClaimsAsync(user, properties, new[] { sessionClaim });
        }
        catch
        {
            await _loginHistory.EndSessionAsync(sessionId.ToString(), user.Id, LoginHistoryConstants.SignInFailed);
            throw;
        }

        await _userRepository.UpdateLastLoginAsync(user.Id, _clock.GetUtcNow().UtcDateTime);
    }

    private async Task StartPasswordSetupSessionAsync(ApplicationUser user, bool rememberMe, LoginDeviceInput? device)
    {
        // This session only allows the user to change their temporary password.
        await _loginHistory.EndCurrentSessionAsync(Context, LoginHistoryConstants.Replaced);
        await _signInManager.SignOutAsync();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Email!),
            new Claim(PasswordSetupAuthentication.StampClaim, user.SecurityStamp!),
            new Claim(PasswordSetupAuthentication.RememberClaim, rememberMe.ToString())
        };

        var pendingDevice = _loginMetadata.SerializePendingDevice(device);
        if (pendingDevice is not null)
        {
            claims.Add(new Claim(LoginMetadataService.PendingDeviceClaim, pendingDevice));
        }

        var identity = new ClaimsIdentity(claims, PasswordSetupAuthentication.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = _clock.GetUtcNow().AddMinutes(10)
        };

        await Context.SignInAsync(PasswordSetupAuthentication.Scheme, principal, properties);
    }

    private bool IsTemporaryPasswordExpired(ApplicationUser user)
    {
        if (user.TemporaryPasswordExpiresAt is null)
        {
            return true;
        }

        return user.TemporaryPasswordExpiresAt <= _clock.GetUtcNow().UtcDateTime;
    }

    private void PreparePasswordChange(ApplicationUser user)
    {
        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAt = null;
        user.PasswordSetAt = _clock.GetUtcNow().UtcDateTime;
        user.ModifiedBy = user.Id;
        user.ModifiedAt = _clock.GetUtcNow().UtcDateTime;
        user.EmailConfirmed = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
    }

    private static OperationResult PasswordFailure(IdentityResult result)
    {
        var errors = result.Errors.Select(error => error.Description).ToArray();
        return OperationResult.Failure(errors);
    }

    private static OperationResult InvalidLink()
    {
        return OperationResult.Failure("This link is invalid or expired. Request a new password link.");
    }
}
