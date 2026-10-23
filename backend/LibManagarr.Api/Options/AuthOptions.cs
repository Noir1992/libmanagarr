namespace LibManagarr.Api.Options;

/// <summary>
/// Authentication transport switch. RequireSecureCookies forces the Secure flag on the auth
/// and CSRF cookies even when the request itself arrives over plain HTTP, for example when
/// TLS terminates at a trusted proxy configured under ForwardedHeaders.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool RequireSecureCookies { get; init; }
}
