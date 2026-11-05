namespace LibManagarr.Api.Models.Auth;

/// <summary>
/// Names the credential a failed proof was about, so the UI can point at the right field and word
/// the answer correctly instead of reading the human-readable title of the problem.
/// </summary>
public static class AuthFailureKinds
{
    /// <summary>The password of the local account, asked in the login form.</summary>
    public const string Password = "password";

    /// <summary>A two-factor or recovery code, asked in a login step or an account form.</summary>
    public const string SecondFactor = "second_factor";

    /// <summary>
    /// The password of the local account asked by a backup form. It proves the same identity as a
    /// password change does, but the UI marks the field of the backup form, which is why it is not
    /// reported as a plain password failure. The password that decrypts an imported archive is a
    /// different credential and never reaches this kind: it is reported as its own archive error
    /// and cannot lock the local account.
    /// </summary>
    public const string BackupPassword = "backup_password";
}
