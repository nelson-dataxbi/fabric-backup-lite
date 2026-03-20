using Fabric_backup_lite.Core.Models;

namespace Fabric_backup_lite.Core.Services;

public interface IOneLakeDownloadService
{
    bool RequiresOneLakeDownload(FabricItemType itemType);

    Task<int> DownloadItemAsync(
        string workspaceId,
        FabricItem item,
        string backupRootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
