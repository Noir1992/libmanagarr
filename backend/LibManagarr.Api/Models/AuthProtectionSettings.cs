namespace LibManagarr.Api.Models;

/// <summary>
/// Tunable half of the sign-in protection. The request budget is enforced per source IP across
/// every rate limited auth, account and backup route, while the failure budget belongs to the
/// account and is shared by all identity proofs. The lockout duration itself stays fixed.
/// </summary>
public sealed class AuthProtectionSettings
{
    public int RequestsPerMinute { get; set; } = 10;

    public int MaxFailedAttempts { get; set; } = 5;
}
