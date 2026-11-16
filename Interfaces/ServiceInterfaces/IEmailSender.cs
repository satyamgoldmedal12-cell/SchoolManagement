namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface IEmailSender
{
    Task SendPasswordLinkAsync(string email, string name, string url, bool invitation);
    Task SendTemporaryCredentialsAsync(string email, string name, string password, string loginUrl, DateTime expiresAt);
}
