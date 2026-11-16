namespace SchoolManagement.Services;

public static class LoginHistoryConstants
{
    public const string SessionClaim = "school:login-history-session";

    public const string Logout = "Logout";
    public const string Expired = "Expired";
    public const string Replaced = "Replaced";
    public const string PasswordReset = "PasswordReset";
    public const string AccountDisabled = "AccountDisabled";
    public const string Revoked = "Revoked";
    public const string SignInFailed = "SignInFailed";
}
