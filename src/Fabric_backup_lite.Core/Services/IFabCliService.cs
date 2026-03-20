namespace Fabric_backup_lite.Core.Services;

public interface IFabCliService
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<string?> ExportItemAsync(
        string workspaceName,
        string itemName,
        string itemType,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
