using System.IO;
using Azure.Core;
using Azure.Storage.Files.DataLake;
using Fabric_backup_lite.Core.Models;
using Microsoft.Extensions.Logging;

namespace Fabric_backup_lite.Core.Services;

public class OneLakeDownloadService : IOneLakeDownloadService
{
    private readonly IAuthenticationService _authService;
    private readonly FileSystemService _fileSystem;
    private readonly ILogger<OneLakeDownloadService> _logger;

    private const string OneLakeEndpoint = "https://onelake.dfs.fabric.microsoft.com";

    public OneLakeDownloadService(
        IAuthenticationService authService,
        FileSystemService fileSystem,
        ILogger<OneLakeDownloadService> logger)
    {
        _authService = authService;
        _fileSystem  = fileSystem;
        _logger      = logger;
    }

    public bool RequiresOneLakeDownload(FabricItemType itemType) =>
        itemType == FabricItemType.Warehouse;

    public async Task<int> DownloadItemAsync(
        string workspaceId,
        FabricItem item,
        string backupRootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting OneLake download for {Type} '{Name}' ({Id})",
            item.Type, item.DisplayName, item.Id);

        var storageToken = await _authService.GetStorageTokenAsync(cancellationToken);
        var credential   = new BearerTokenCredential(storageToken);

        var serviceUri       = new Uri(OneLakeEndpoint);
        var serviceClient    = new DataLakeServiceClient(serviceUri, credential);
        var fileSystemClient = serviceClient.GetFileSystemClient(workspaceId);

        var sanitizedName = _fileSystem.SanitizeFileName(item.DisplayName);
        var timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var subFolder     = item.Type == FabricItemType.Warehouse ? "Warehouses" : "Lakehouses";
        var localItemPath = Path.Combine(backupRootPath, subFolder, $"{sanitizedName}_{timestamp}");
        Directory.CreateDirectory(localItemPath);

        int fileCount = 0;

        await foreach (var pathItem in fileSystemClient.GetPathsAsync(
            path: item.Id, recursive: true, cancellationToken: cancellationToken))
        {
            if (pathItem.IsDirectory == true)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = pathItem.Name;
            if (relativePath.StartsWith(item.Id, StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath[(item.Id.Length)..].TrimStart('/');

            var localFilePath = Path.Combine(localItemPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(localFilePath)!);

            progress?.Report($"Downloading {relativePath}");
            _logger.LogDebug("Downloading {Path} → {LocalPath}", pathItem.Name, localFilePath);

            var fileClient = fileSystemClient.GetFileClient(pathItem.Name);
            var response   = await fileClient.ReadAsync(cancellationToken: cancellationToken);

            await using var fileStream = File.Create(localFilePath);
            await response.Value.Content.CopyToAsync(fileStream, cancellationToken);

            fileCount++;
        }

        _logger.LogInformation(
            "OneLake download complete for '{Name}': {Count} files → {Path}",
            item.DisplayName, fileCount, localItemPath);

        return fileCount;
    }
}

/// <summary>
/// Wraps a pre-obtained bearer token string into the Azure.Core TokenCredential contract.
/// The expiry is set conservatively to 55 minutes from creation; MSAL will refresh before this.
/// </summary>
internal sealed class BearerTokenCredential : TokenCredential
{
    private readonly AccessToken _token;

    public BearerTokenCredential(string tokenValue)
    {
        _token = new AccessToken(tokenValue, DateTimeOffset.UtcNow.AddMinutes(55));
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => _token;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new ValueTask<AccessToken>(_token);
}
