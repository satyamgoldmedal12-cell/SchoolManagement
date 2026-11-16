using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface IAccountService
{
    Task<LoginOutcome> LoginAsync(string email, string password, bool rememberMe, LoginDeviceInput? device = null);
    Task<OperationResult> ChangeTemporaryPasswordAsync(string password, LoginDeviceInput? device = null);
    Task RequestPasswordResetAsync(string email);
    Task<OperationResult> ResetPasswordAsync(string userId, string code, string password);
    Task SignOutAsync();
}
