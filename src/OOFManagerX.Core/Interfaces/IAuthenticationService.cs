namespace OOFManagerX.Core.Interfaces;

/// <summary>
/// Service for Microsoft 365 authentication.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Gets whether the user is currently signed in.
    /// </summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// Gets the current user's email/UPN.
    /// </summary>
    string? CurrentUserPrincipalName { get; }

    /// <summary>
    /// Signs in the user interactively.
    /// </summary>
    Task<AuthResult> SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts silent sign-in using cached credentials.
    /// </summary>
    Task<AuthResult> TrySilentSignInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs out the current user.
    /// </summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a valid access token for Graph API calls.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an authentication attempt.
/// </summary>
public record AuthResult
{
    public bool Success { get; init; }
    public string? UserPrincipalName { get; init; }
    public string? UserId { get; init; }
    public string? ErrorMessage { get; init; }

    public static AuthResult Succeeded(string upn, string userId) => new()
    {
        Success = true,
        UserPrincipalName = upn,
        UserId = userId
    };

    public static AuthResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
