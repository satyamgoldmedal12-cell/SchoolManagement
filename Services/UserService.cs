using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class UserService : IUserService
{
    private readonly ISchoolRepository _schoolRepository;
    private readonly IUserRepository _userRepository;
    private readonly SqlSession _database;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITemporaryPasswordGenerator _passwordGenerator;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _clock;
    private readonly ILoginHistoryService _loginHistory;

    public UserService(
        ISchoolRepository schoolRepository,
        IUserRepository userRepository,
        SqlSession database,
        UserManager<ApplicationUser> userManager,
        ITemporaryPasswordGenerator passwordGenerator,
        INotificationService notifications,
        TimeProvider clock,
        ILoginHistoryService loginHistory)
    {
        _schoolRepository = schoolRepository;
        _userRepository = userRepository;
        _database = database;
        _userManager = userManager;
        _passwordGenerator = passwordGenerator;
        _notifications = notifications;
        _clock = clock;
        _loginHistory = loginHistory;
    }

    public Task<PagedResult<UserSummary>> GetUsersAsync(string? search, int page, CancellationToken cancellationToken = default)
    {
        return _userRepository.ListUsersAsync(search, page, cancellationToken);
    }

    public Task<IReadOnlyList<LookupItem>> GetDesignationOptionsAsync(CancellationToken cancellationToken = default)
    {
        return _schoolRepository.GetDesignationOptionsAsync(cancellationToken);
    }

    public async Task<OperationResult> CreateAsync(CreateUserCommand command, string actorId)
    {
        var designation = await _schoolRepository.FindDesignationAsync(command.DesignationId);
        if (designation is null || !designation.IsActive)
        {
            return OperationResult.Failure("Choose an active designation.");
        }

        var temporaryPassword = _passwordGenerator.Generate();

        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        var expiresAt  = DateTime.Now.AddHours(0);

        var result = await CreateUserWithRoleAsync(command, designation, temporaryPassword, expiresAt, actorId);

        if (!result.Succeeded)
        {
            return result;
        }

        var user = new UserAccess(result.Id!, command.FullName, command.Email, true, true);
        bool emailSent = await _notifications.SendTemporaryPasswordAsync(user, temporaryPassword, expiresAt);
        if (!emailSent)
        {
            return OperationResult.Success(result.Id,
                "User saved, but email delivery failed. Configure SMTP and retry Send login email; a new temporary password will be generated.");
        }

        await _userRepository.MarkEmailSentAsync(result.Id!, actorId);
        return OperationResult.Success(result.Id);
    }

    public async Task<OperationResult> SendAccessEmailAsync(string id, string actorId)
    {
        var user = await _userRepository.FindUserAsync(id);
        if (user is null)
        {
            return OperationResult.Failure("User not found.");
        }

        if (!user.IsActive)
        {
            return OperationResult.Failure("Activate this user before sending a login email.");
        }

        bool emailSent;
        if (user.MustChangePassword)
        {
            var temporaryPassword = _passwordGenerator.Generate();
            var expiresAt = _clock.GetUtcNow().UtcDateTime.AddHours(24);
            var result = await ReissueTemporaryPasswordAsync(id, temporaryPassword, expiresAt, actorId);
            if (!result.Succeeded)
            {
                return result;
            }

            emailSent = await _notifications.SendTemporaryPasswordAsync(user, temporaryPassword, expiresAt);
        }
        else
        {
            emailSent = await _notifications.SendResetLinkAsync(user);
        }

        if (!emailSent)
        {
            return OperationResult.Success(id, "Email delivery failed. Check SMTP settings and retry.");
        }

        await _userRepository.MarkEmailSentAsync(id, actorId);
        return OperationResult.Success(id);
    }

    public async Task<OperationResult> SetActiveAsync(string id, bool active, byte[] rowVersion, string actorId)
    {
        if (id == actorId)
        {
            return OperationResult.Failure("You cannot change your own account status.");
        }

        if (rowVersion.Length != 8)
        {
            return OperationResult.Failure("Invalid version. Reload the users page.");
        }

        bool updated = await _userRepository.SetUserActiveAsync(id, active, rowVersion, actorId);
        if (!updated)
        {
            return OperationResult.Failure("User changed in another session. Reload the page and retry.");
        }

        if (!active)
        {
            await _loginHistory.EndUserSessionsAsync(id, LoginHistoryConstants.AccountDisabled);
        }

        return OperationResult.Success(id);
    }

    private async Task<OperationResult> CreateUserWithRoleAsync(
        CreateUserCommand command,
        DesignationAccess designation,
        string password,
        DateTime expiresAt,
        string actorId)
    {
        // Save the user and role together; an unsuccessful operation rolls back both.
        await using var transaction = await _database.BeginTransactionAsync();

        bool isValidDesignation = await _userRepository.IsActiveDesignationAsync(designation.Id, designation.RoleId);

        if (!isValidDesignation)
        {
            return OperationResult.Failure("Choose an active designation.");
        }

        var email = command.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = command.FullName.Trim(),
            PhoneNumber = command.PhoneNumber?.Trim(),
            DesignationId = designation.Id,
            CreatedBy = actorId,
            MustChangePassword = true,
            TemporaryPasswordExpiresAt = expiresAt
        };

        try
        {
            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                return Failure(createResult);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, designation.RoleName);
            if (!roleResult.Succeeded)
            {
                return Failure(roleResult);
            }

            await transaction.CommitAsync();
            return OperationResult.Success(user.Id);
        }
        catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
        {
            return OperationResult.Failure("This email address already exists.");
        }
    }

    private async Task<OperationResult> ReissueTemporaryPasswordAsync(
        string id,
        string password,
        DateTime expiresAt,
        string? actorId)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null || !user.IsActive || user.DeletedAt is not null || !user.MustChangePassword)
        {
            return OperationResult.Failure("Only an active user awaiting their first password can receive new temporary credentials.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        user.TemporaryPasswordExpiresAt = expiresAt;
        user.InvitationSentAt = null;
        user.ModifiedBy = actorId;
        user.ModifiedAt = DateTime.UtcNow;

        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            return Failure(result);
        }

        return OperationResult.Success(id);
    }

    private static OperationResult Failure(IdentityResult result)
    {
        return OperationResult.Failure(result.Errors.Select(error => error.Description).ToArray());
    }
}
