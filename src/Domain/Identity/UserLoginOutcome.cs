namespace ErpApp.Domain.Identity;

/// <summary>
/// What a <see cref="UserLoginEvent"/> records. The three members are exactly the three
/// Description values the reference product's User Log prints -- "Login Success", "Logout Success"
/// and "Login Fail" (the first two confirmed live on 2026-09-03, the third recorded in the
/// 2026-09-02 catalogue pass).
/// </summary>
public enum UserLoginOutcome
{
    LoginSucceeded = 0,
    LoginFailed = 1,
    LogoutSucceeded = 2,
}
