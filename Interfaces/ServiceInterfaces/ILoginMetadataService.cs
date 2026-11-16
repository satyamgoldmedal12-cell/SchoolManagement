using System.Security.Claims;
using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface ILoginMetadataService
{
    void ApplyTo(UserLoginHistory session, HttpContext context, LoginDeviceInput? input);
    string? SerializePendingDevice(LoginDeviceInput? input);
    LoginDeviceInput MergePendingDevice(ClaimsPrincipal? principal, LoginDeviceInput? input);
}
