using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using OOFManagerX.Core.Interfaces;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Authentication service using MSAL with Windows Account Manager (WAM).
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private const string ClientId = "c0eceb27-8cd3-4bb8-9271-c90596069f74";
    private static readonly string[] Scopes = ["user.read", "MailboxSettings.ReadWrite"];

    private readonly ILogger<AuthenticationService> _logger;
    private readonly IPublicClientApplication _publicClientApp;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    
    private AuthenticationResult? _authResult;
    private string? _cachedUpn;

    public bool IsSignedIn => _authResult?.ExpiresOn > DateTimeOffset.UtcNow;
    public string? CurrentUserPrincipalName => _cachedUpn ?? _authResult?.Account?.Username;

    public AuthenticationService(ILogger<AuthenticationService> logger, Func<IntPtr>? windowHandleProvider = null)
    {
        _logger = logger;

        var builder = PublicClientApplicationBuilder.Create(ClientId)
            .WithDefaultRedirectUri()
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));

        if (windowHandleProvider != null)
        {
            builder.WithParentActivityOrWindow(windowHandleProvider);
        }

        _publicClientApp = builder.Build();
        
        // Enable token cache serialization
        EnableTokenCacheSerialization();
    }

    public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Attempting interactive sign-in");

            // Try silent first
            var silentResult = await TrySilentSignInInternalAsync(cancellationToken);
            if (silentResult.Success)
            {
                return silentResult;
            }

            // Fall back to interactive
            _authResult = await _publicClientApp
                .AcquireTokenInteractive(Scopes)
                .WithPrompt(Prompt.NoPrompt)
                .ExecuteAsync(cancellationToken);

            _cachedUpn = _authResult.Account.Username;
            _logger.LogInformation("Interactive sign-in successful for {User}", _cachedUpn);

            return AuthResult.Succeeded(_authResult.Account.Username, _authResult.UniqueId);
        }
        catch (MsalException ex)
        {
            _logger.LogError(ex, "MSAL authentication failed");
            return AuthResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected authentication error");
            return AuthResult.Failed(ex.Message);
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<AuthResult> TrySilentSignInAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            return await TrySilentSignInInternalAsync(cancellationToken);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<AuthResult> TrySilentSignInInternalAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting silent sign-in");

        try
        {
            var accounts = await _publicClientApp.GetAccountsAsync();
            var account = accounts.FirstOrDefault(a => 
                _cachedUpn == null || 
                a.Username.Equals(_cachedUpn, StringComparison.OrdinalIgnoreCase));

            if (account == null)
            {
                _logger.LogInformation("No cached account found for silent sign-in");
                return AuthResult.Failed("No cached account");
            }

            _authResult = await _publicClientApp
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync(cancellationToken);

            _cachedUpn = _authResult.Account.Username;
            _logger.LogInformation("Silent sign-in successful for {User}", _cachedUpn);

            return AuthResult.Succeeded(_authResult.Account.Username, _authResult.UniqueId);
        }
        catch (MsalUiRequiredException)
        {
            _logger.LogInformation("Silent sign-in requires UI interaction");
            return AuthResult.Failed("UI required");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Silent sign-in failed (transient)");
            return AuthResult.Failed(ex.Message);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            var accounts = await _publicClientApp.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _publicClientApp.RemoveAsync(account);
            }

            _authResult = null;
            _cachedUpn = null;
            _logger.LogInformation("User signed out successfully");
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Ensure we have a valid token
        if (_authResult == null || _authResult.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var result = await TrySilentSignInAsync(cancellationToken);
            if (!result.Success)
            {
                return null;
            }
        }

        return _authResult?.AccessToken;
    }

    private void EnableTokenCacheSerialization()
    {
        var cacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OOFManagerX",
            "msal_cache.bin");

        var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrEmpty(cacheDirectory) && !Directory.Exists(cacheDirectory))
        {
            Directory.CreateDirectory(cacheDirectory);
        }

        _publicClientApp.UserTokenCache.SetBeforeAccess(args =>
        {
            try
            {
                if (File.Exists(cacheFilePath))
                {
                    var encryptedData = File.ReadAllBytes(cacheFilePath);
                    var data = System.Security.Cryptography.ProtectedData.Unprotect(
                        encryptedData, null, 
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    args.TokenCache.DeserializeMsalV3(data);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load token cache");
            }
        });

        _publicClientApp.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged)
            {
                try
                {
                    var data = args.TokenCache.SerializeMsalV3();
                    var encryptedData = System.Security.Cryptography.ProtectedData.Protect(
                        data, null, 
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(cacheFilePath, encryptedData);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save token cache");
                }
            }
        });
    }
}
