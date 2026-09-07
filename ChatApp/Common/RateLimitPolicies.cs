namespace ChatApp.Common;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Register = "register";
    public const string RefreshToken = "refresh-token";
    public const string ForgotPassword = "forgot-password";
    public const string ResetPassword = "reset-password";
    public const string GoogleLogin = "google-login";
    public const string ResolveFriendLink = "resolve-friend-link";
    public const string GetMessages = "get-messages";
}
