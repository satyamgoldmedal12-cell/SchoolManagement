using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface IGeoLocationService
{
    Task ApplyAsync(UserLoginHistory session, CancellationToken cancellationToken = default);
}
