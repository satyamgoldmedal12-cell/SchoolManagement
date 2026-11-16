namespace SchoolManagement.Services;

public static class PasswordSetupAuthentication
{
    public const string Scheme = "SchoolManagement.PasswordSetup";
    public const string StampClaim = "school:setup-stamp";
    public const string RememberClaim = "school:remember";
}
