using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface ILoginAreaService
{
    void ApplyTo(UserLoginHistory session);
}
