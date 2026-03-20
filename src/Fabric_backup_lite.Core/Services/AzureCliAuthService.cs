using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace Fabric_backup_lite.Core.Services;

public class AzureCliAuthService : IAuthenticationService
{
    private readonly AzureCliCredential _credential;
    private readonly ILogger<AzureCliAuthService> _logger;
    private string? _tenantId;

    public AzureCliAuthService(ILogger<AzureCliAuthService> logger)
    {
        _credential = new AzureCliCredential();
        _logger = logger;
    }

    public bool IsAuthenticated => true;
    public string? UserDisplayName => "az login user";

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokenResult = await _credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(["https://api.fabric.microsoft.com/.default"]),
            cancellationToken);
        return tokenResult.Token;
    }

    public async Task<string> GetStorageTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokenResult = await _credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(["https://storage.azure.com/.default"]),
            cancellationToken);
        return tokenResult.Token;
    }

    public async Task<string> GetTenantIdAsync()
    {
        if (_tenantId is not null)
            return _tenantId;

        var tokenResult = await _credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(["https://api.fabric.microsoft.com/.default"]),
            default);

        var parts = tokenResult.Token.Split('.');
        if (parts.Length >= 2)
        {
            var payload = parts[1];
            var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tid", out var tid))
            {
                _tenantId = tid.GetString() ?? string.Empty;
                return _tenantId;
            }
        }

        _logger.LogWarning("Could not extract tenant ID from token; returning empty string.");
        _tenantId = string.Empty;
        return _tenantId;
    }

    public Task SignInAsync() => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
