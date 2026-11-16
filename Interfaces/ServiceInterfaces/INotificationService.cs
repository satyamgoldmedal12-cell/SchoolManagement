using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface INotificationService
{
    string LoginUrl { get; }
    Task<bool> SendTemporaryPasswordAsync(UserAccess user, string password, DateTime expiresAt);
    Task<bool> SendResetLinkAsync(UserAccess user);
}
