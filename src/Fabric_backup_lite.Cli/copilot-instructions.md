# Copilot Instructions — Fabric Backup Lite CLI

## What this project is

`Fabric_backup_lite.Cli` is a .NET 8 console application (`fbl`) that exposes all Fabric Backup Lite functionality from the command line. It references `Fabric_backup_lite.Core` for all business logic and services.

---

## Project structure

```
src/
├── Fabric_backup_lite.Core/         ← shared services + models (no WPF dependency)
│   ├── Models/                      ← FabricItem, Workspace, BackupMetadata, etc.
│   └── Services/                    ← IAuthenticationService, IBackupService, etc.
│       ├── AzureCliAuthService.cs   ← NEW: az login auth
│       └── ServicePrincipalAuthService.cs ← NEW: client credentials auth
└── Fabric_backup_lite.Cli/
    ├── Fabric_backup_lite.Cli.csproj
    ├── appsettings.json
    ├── Program.cs                   ← entry point, DI setup, command routing
    └── Commands/
        ├── BackupCommand.cs
        ├── RestoreCommand.cs
        └── ListCommand.cs
```

---

## CLI commands specification

### Global auth options (apply to all commands)

| Option | Description | Default |
|---|---|---|
| `--auth` | Auth mode: `az-login` or `sp` | `az-login` |
| `--client-id` | Service principal client ID | — |
| `--client-secret` | Service principal client secret | — |
| `--tenant-id` | Tenant ID for service principal | — |

Auth options can also be set via environment variables:
- `FABRIC_AUTH` → `az-login` or `sp`
- `FABRIC_CLIENT_ID`
- `FABRIC_CLIENT_SECRET`
- `FABRIC_TENANT_ID`

---

### `fbl backup`

Backs up Fabric/Power BI artifacts to a local folder.

```bash
fbl backup --workspace <name-or-id> --dest <path> [--item-types <types>]
fbl backup --all --dest <path> [--item-types <types>]
```

| Option | Required | Description |
|---|---|---|
| `--workspace` | Yes (or `--all`) | Workspace name or GUID. If name matches multiple workspaces, exits with error and lists matching IDs. |
| `--all` | Yes (or `--workspace`) | Backup all workspaces accessible to the authenticated identity. |
| `--dest` | Yes | Destination folder path. |
| `--item-types` | No | Comma-separated list of item types to include. Valid values: `Report`, `SemanticModel`, `Notebook`, `DataPipeline`, `Dataflow`, `Lakehouse`, `Warehouse`, `KQLDatabase`, `Eventhouse`, `Environment`, `SparkJobDefinition`. Default: all types. |

**Output:** progress per item to stdout. On completion, prints summary (total items, succeeded, failed).

**Exit codes:** `0` = success, `1` = partial failure (some items failed), `2` = fatal error.

---

### `fbl restore`

Restores artifacts from a local backup folder to a Fabric workspace.

```bash
fbl restore --source <path> --workspace <name-or-id>
fbl restore --source <path> --new-workspace <name> --capacity <id>
```

| Option | Required | Description |
|---|---|---|
| `--source` | Yes | Path to backup folder (the one containing `manifest.json`). |
| `--workspace` | Yes (or `--new-workspace`) | Target workspace name or GUID. |
| `--new-workspace` | Yes (or `--workspace`) | Name for a new workspace to create. |
| `--capacity` | With `--new-workspace` | Fabric capacity ID for the new workspace. |
| `--item-types` | No | Comma-separated list to filter which types to restore. |

**Behavior:**
- Reads `manifest.json` from `--source`.
- Warehouses are skipped (non-restorable via API) with a warning message.
- If an item already exists in the target workspace (409 Conflict), logs a warning and continues.

**Exit codes:** same as `backup`.

---

### `fbl list workspaces`

Lists all workspaces accessible to the authenticated identity.

```bash
fbl list workspaces [--output table|json]
```

| Option | Default | Description |
|---|---|---|
| `--output` | `table` | Output format. `table` = human-readable, `json` = machine-readable. |

---

### `fbl list items`

Lists all items in a workspace.

```bash
fbl list items --workspace <name-or-id> [--output table|json]
```

| Option | Required | Description |
|---|---|---|
| `--workspace` | Yes | Workspace name or GUID. |
| `--output` | No | `table` (default) or `json`. |

---

## Authentication implementation

### `AzureCliAuthService` (default for CLI)

Uses `Azure.Identity.AzureCliCredential`. Requires the user to have run `az login` beforehand.

```csharp
// NuGet: Azure.Identity
var credential = new AzureCliCredential();
var tokenResult = await credential.GetTokenAsync(
    new TokenRequestContext(["https://api.fabric.microsoft.com/.default"]),
    cancellationToken);
return tokenResult.Token;
```

For OneLake/Storage token, use the same credential with scope `https://storage.azure.com/.default`.

### `ServicePrincipalAuthService` (for CI/CD with `--auth sp`)

Uses MSAL `ConfidentialClientApplicationBuilder`. No browser, no user interaction.

```csharp
// NuGet: Microsoft.Identity.Client (already in Core)
_msalClient = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(clientSecret)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .Build();

var result = await _msalClient
    .AcquireTokenForClient(["https://api.fabric.microsoft.com/.default"])
    .ExecuteAsync(cancellationToken);
return result.AccessToken;
```

For OneLake/Storage token, call `AcquireTokenForClient` with scope `https://storage.azure.com/.default`.

### `AuthServiceFactory`

Selects the correct implementation based on `--auth` flag or `FABRIC_AUTH` env var:

```csharp
IAuthenticationService Create(AuthOptions options) => options.AuthMode switch
{
    AuthMode.AzureCli       => new AzureCliAuthService(logger),
    AuthMode.ServicePrincipal => new ServicePrincipalAuthService(
                                     options.ClientId!,
                                     options.ClientSecret!,
                                     options.TenantId!,
                                     logger),
    _ => throw new ArgumentException("Unknown auth mode")
};
```

---

## CLI framework

Use **`System.CommandLine`** (version `2.0.0-beta4`).

- Define a root command with global options for auth.
- Subcommands: `backup`, `restore`, `list` (with sub-subcommands `workspaces`, `items`).
- Use `SetHandler` for command execution.
- `--help` / `-h` is generated automatically per command. `--version` is generated automatically on the root command.

---

## Versioning

The CLI version is managed with **MinVer** — a NuGet package that derives the version automatically from git tags. No manual edits to `.csproj` needed.

Add to `Fabric_backup_lite.Cli.csproj`:

```xml
<PackageReference Include="MinVer" Version="5.0.0" PrivateAssets="All" />
```

Release workflow:
1. Develop and commit normally
2. When ready to release: `git tag v1.0.0`
3. `dotnet publish` embeds the version automatically
4. `fbl --version` returns the correct version (read by `System.CommandLine` from the assembly)

Between tags, MinVer produces pre-release versions (e.g. `1.0.1-alpha.5`) automatically. Do **not** set `<Version>` manually in the `.csproj` — MinVer manages it.

---

## Configuration

Read from `appsettings.json` (same keys as WPF app):

```json
{
  "Fabric": {
    "BaseUrl": "https://api.fabric.microsoft.com/v1/",
    "Timeout": 120,
    "RetryAttempts": 3,
    "LROPollingInterval": 2000
  }
}
```

Auth config comes exclusively from CLI flags or environment variables — **not** from `appsettings.json`.

---

## Progress output

Use Serilog console sink with a simple format for CLI output:
```
[HH:mm:ss] Backing up Notebook 'My Analysis'... OK
[HH:mm:ss] Backing up Report 'Sales Dashboard'... OK
[HH:mm:ss] Backing up SemanticModel 'Finance Model'... FAILED (GetDefinition failed: 400)
```

At the end, print a summary:
```
Backup complete: 12 succeeded, 1 failed, 0 skipped.
```

---

## NuGet packages (Cli project)

| Package | Version | Purpose |
|---|---|---|
| `System.CommandLine` | `2.0.0-beta4` | CLI argument parsing |
| `Serilog.Sinks.Console` | `5.0.1` | Console output |
| `Microsoft.Extensions.Hosting` | `8.0.0` | DI + config |
| `MinVer` | `5.0.0` | Version from git tags (`PrivateAssets="All"`) |

## NuGet packages (Core project, additions)

| Package | Version | Purpose |
|---|---|---|
| `Azure.Identity` | latest stable | `AzureCliCredential` |

---

## Distribution

The CLI is distributed as a **single self-contained `fbl.exe`** — no installer, no .NET runtime required on the target machine.

Published with:
```
dotnet publish -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:AssemblyName=fbl
```

The output `fbl.exe` is uploaded as an asset to the GitHub Release alongside `FabricBackupLite.msi`.
A helper script `installer/PublishCli.ps1` automates the publish step.
