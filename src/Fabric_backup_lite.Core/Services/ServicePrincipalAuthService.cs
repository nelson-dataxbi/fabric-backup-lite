using Microsoft.Identity.Client;
using Microsoft.Extensions.Logging;

namespace Fabric_backup_lite.Core.Services;

public class ServicePrincipalAuthService : IAuthenticationService
{
    private readonly IConfidentialClientApplication _msalClient;
    private readonly string _tenantId;
    private readonly ILogger<ServicePrincipalAuthService> _logger;

    public ServicePrincipalAuthService(
        string clientId,
        string clientSecret,
        string tenantId,
        ILogger<ServicePrincipalAuthService> logger)
    {
        _tenantId = tenantId;
        _logger = logger;
        _msalClient = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();
    }

    public bool IsAuthenticated => true;
    public string? UserDisplayName => "service-principal";

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var result = await _msalClient
            .AcquireTokenForClient(["https://api.fabric.microsoft.com/.default"])
            .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    public async Task<string> GetStorageTokenAsync(CancellationToken cancellationToken = default)
    {
        var result = await _msalClient
            .AcquireTokenForClient(["https://storage.azure.com/.default"])
            .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    public Task<string> GetTenantIdAsync() => Task.FromResult(_tenantId);

    public Task SignInAsync() => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
