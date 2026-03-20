# fbl — Fabric Backup Lite CLI

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?logo=windows" alt="Windows"/>
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"/>
  <img src="https://img.shields.io/badge/lang-ES%20%7C%20EN-orange" alt="ES | EN"/>
</p>

<p align="center">
  <a href="#español">🇪🇸 Español</a> &nbsp;|&nbsp; <a href="#english">🇺🇸 English</a>
</p>

---

## Español

`fbl` es la interfaz de línea de comandos de **Fabric Backup Lite**. Permite hacer backup y restore de workspaces de Microsoft Fabric desde la terminal, habilitando automatización y scripts que complementan la aplicación de escritorio Windows.

### ¿Qué hace?

Se conecta a Microsoft Fabric mediante la API REST oficial y permite:

| Comando | Descripción |
|---|---|
| `fbl backup` | Descarga las definiciones de todos los ítems de un workspace a carpetas locales |
| `fbl restore` | Recrea los ítems de un backup en un workspace existente o nuevo |
| `fbl list workspaces` | Lista todos los workspaces accesibles en el tenant |
| `fbl list items` | Lista los ítems de un workspace |
| `fbl list backups` | Descubre y lista los backups disponibles en una carpeta local |

### Instalación

#### Opción A — Descargar el ejecutable

1. Descarga `fbl-*-win-x64.zip` desde la sección [**Releases**](https://github.com/nelson-dataxbi/fabric-backup-lite/releases) del repositorio
2. Extrae con **PowerShell** (importante: no uses el Explorador de Windows):

```powershell
Expand-Archive fbl-v0.1.1-cli-win-x64.zip -DestinationPath C:\Tools\fbl

# Agregar al PATH (ejecutar una sola vez)
[Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";C:\Tools\fbl", "User")
```

3. Verifica la instalación:

> **Nota:** si extraes el zip con el Explorador de Windows en lugar de PowerShell, Windows puede mostrar una alerta de seguridad al ejecutar `fbl.exe` por primera vez. Para ignorarla: clic en **"Más información"** → **"Ejecutar de todas formas"**. El exe es seguro — el código fuente es público en este repositorio.

```
fbl --version
```

> No se requiere instalar .NET — el ejecutable es autocontenido.

#### Opción B — Compilar desde el código fuente

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) y [Git](https://git-scm.com/)

```powershell
git clone https://github.com/wcalcagno/fabric-backup-lite.git
cd fabric-backup-lite
.\installer\PublishCli.ps1
# El ejecutable queda en: dist\cli\fbl.exe
```

### Autenticación

`fbl` soporta dos modos de autenticación:

| Modo | Flag | Caso de uso |
|---|---|---|
| `az login` (por defecto) | `--auth az-login` | Uso interactivo — requiere tener [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) instalada y haber ejecutado `az login` |
| Service Principal | `--auth sp` | Automatización, CI/CD, ejecución desatendida |

#### Modo az-login (interactivo)

```powershell
az login
fbl list workspaces
```

Si tu cuenta tiene MFA, el login se abre en el navegador la primera vez. Las credenciales quedan cacheadas.

#### Modo Service Principal

```powershell
fbl list workspaces --auth sp --client-id <id> --client-secret <secret> --tenant-id <tenant>
```

También se pueden definir mediante variables de entorno para no repetirlas en cada comando:

```powershell
$env:FABRIC_AUTH          = "sp"
$env:FABRIC_CLIENT_ID     = "<id>"
$env:FABRIC_CLIENT_SECRET = "<secret>"
$env:FABRIC_TENANT_ID     = "<tenant>"

fbl list workspaces
```

##### Permisos requeridos para Service Principal

El App Registration en Microsoft Entra ID necesita estos permisos de **aplicación** en la API de Power BI Service:

| Permiso | Necesario para |
|---|---|
| `Workspace.Read.All` | Listar workspaces y ítems, backup |
| `Item.ReadAll` | Leer definiciones de ítems (backup) |
| `Workspace.ReadWrite.All` | Crear workspaces (restore con `--new-workspace`) |
| `Item.ReadWrite.All` | Crear ítems (restore) |

---

### Comandos

#### Opciones globales de autenticación

Disponibles en todos los comandos:

| Opción | Descripción | Por defecto |
|---|---|---|
| `--auth` | Modo de autenticación: `az-login` o `sp` | `az-login` |
| `--client-id` | Client ID del service principal | — |
| `--client-secret` | Client secret del service principal | — |
| `--tenant-id` | Tenant ID del directorio | — |

---

#### `fbl list workspaces`

Lista todos los workspaces accesibles en el tenant.

```
fbl list workspaces [--output table|json]
```

```
ID                                      Name
--------------------------------------------------------------------------------
0e1404ef-2576-49d0-b3b2-7cc63c3665a2    Taller_Fabric_2026-03_00
...

309 workspace(s) found.
```

---

#### `fbl list items`

Lista todos los ítems de un workspace.

```
fbl list items --workspace <nombre-o-id> [--output table|json]
```

| Opción | Requerida | Descripción |
|---|---|---|
| `--workspace` | Sí | Nombre o GUID del workspace |
| `--output` | No | `table` (por defecto) o `json` |

```
Type                    Name
--------------------------------------------------------------------------------
DataPipeline            PL_ETL
Notebook                NB_INGST_TasasCambio
SemanticModel           DW-Ana
...

32 item(s) in workspace 'Taller_Fabric_2026-03_00'.
```

---

#### `fbl list backups`

Descubre recursivamente todos los backups disponibles bajo una carpeta raíz y los muestra ordenados del más reciente al más antiguo, con un índice numérico utilizable en `fbl restore`.

```
fbl list backups --root <ruta> [--output table|json]
```

| Opción | Requerida | Descripción |
|---|---|---|
| `--root` | Sí | Carpeta raíz donde buscar backups |
| `--output` | No | `table` (por defecto) o `json` |

```
#    Workspace                       Date                 Items
--------------------------------------------------------------------
1    Taller_Fabric_2026-03_00        2026-03-20 14:55:19     16
2    Taller_Fabric_2026-03_00        2026-03-19 09:12:44     16
3    dataXbi                         2026-03-18 17:30:01      8

3 backup(s) found.
```

> No requiere autenticación — lee únicamente archivos locales.

---

#### `fbl backup`

Hace backup de un workspace completo o de todos los workspaces accesibles.

```
fbl backup --workspace <nombre-o-id> --dest <ruta> [--item-types <tipos>]
fbl backup --all --dest <ruta> [--item-types <tipos>]
```

| Opción | Requerida | Descripción |
|---|---|---|
| `--workspace` | Sí (o `--all`) | Nombre o GUID del workspace |
| `--all` | Sí (o `--workspace`) | Hace backup de todos los workspaces en subcarpetas separadas |
| `--dest` | Sí | Carpeta destino del backup |
| `--item-types` | No | Filtro separado por comas: `Notebook`, `SemanticModel`, `Report`, `DataPipeline`, `Dataflow`, `Lakehouse`, `Warehouse`, `KQLDatabase`, `Eventhouse`, `Environment`, `SparkJobDefinition` |

**Exit codes:** `0` éxito · `1` falla parcial (algunos ítems fallaron) · `2` error fatal

**Ejemplo:**

```powershell
# Backup completo de un workspace
fbl backup --workspace Taller_Fabric_2026-03_00 --dest backups/

# Solo notebooks y semantic models
fbl backup --workspace Taller_Fabric_2026-03_00 --dest backups/ --item-types Notebook,SemanticModel

# Backup de todos los workspaces
fbl backup --all --dest backups/
```

**Estructura generada en disco:**

```
backups/
└── <WorkspaceName>/
    └── <TenantId>/
        └── <WorkspaceName>_<WorkspaceId>/
            └── <UnixTimestampMs>_backup/
                ├── Notebooks/
                ├── SemanticModels/
                ├── DataPipelines/
                └── manifest.json
```

---

#### `fbl restore`

Restaura ítems desde un backup local a un workspace de Fabric.

```
fbl restore --source <ruta>          --workspace <nombre-o-id>
fbl restore --source <ruta>          --new-workspace <nombre> --capacity <id>
fbl restore --root <ruta> --backup <n|latest>  --workspace <nombre-o-id>
fbl restore --root <ruta> --backup <n|latest>  --new-workspace <nombre> --capacity <id>
```

| Opción | Requerida | Descripción |
|---|---|---|
| `--source` | Sí (o `--root`+`--backup`) | Ruta directa a la carpeta del backup (la que contiene `manifest.json`) |
| `--root` | Sí (con `--backup`) | Carpeta raíz donde buscar backups — misma que en `fbl list backups` |
| `--backup` | Sí (con `--root`) | Índice del backup del listado de `fbl list backups`, o `latest` para el más reciente |
| `--workspace` | Sí (o `--new-workspace`) | Workspace destino (nombre o GUID) |
| `--new-workspace` | Sí (o `--workspace`) | Nombre del workspace nuevo a crear |
| `--capacity` | Con `--new-workspace` | ID de la capacity de Fabric para el nuevo workspace |
| `--item-types` | No | Filtra los tipos de ítem a restaurar |

**Exit codes:** `0` éxito · `1` falla parcial · `2` error fatal

**Notas:**
- `--source` y `--root`/`--backup` son mutuamente excluyentes
- Los Warehouses se omiten con advertencia — no son restaurables vía API
- Si un ítem ya existe en el workspace destino se registra como advertencia y continúa con los demás

**Ejemplos:**

```powershell
# Restaurar el backup más reciente al workspace original
fbl restore --root backups/ --backup latest --workspace Taller_Fabric_2026-03_00

# Restaurar el segundo backup más reciente a un workspace distinto
fbl restore --root backups/ --backup 2 --workspace Taller_Fabric_2026-03_00_R

# Restaurar a un nuevo workspace (creándolo en el momento)
fbl restore --root backups/ --backup latest --new-workspace MiWorkspaceNuevo --capacity <capacity-id>

# Restaurar desde ruta directa
fbl restore --source "backups/Taller_Fabric_2026-03_00/.../1774018452734_backup" --workspace Taller_Fabric_2026-03_00_R

# Restaurar solo notebooks
fbl restore --root backups/ --backup latest --workspace Taller_Fabric_2026-03_00_R --item-types Notebook
```

---

### Flujo de trabajo típico

```powershell
# 1. Hacer backup
fbl backup --workspace MiWorkspace --dest backups/

# 2. Ver backups disponibles
fbl list backups --root backups/

# 3. Restaurar el más reciente en un workspace de prueba
fbl restore --root backups/ --backup latest --workspace MiWorkspace_Test
```

---

## English

`fbl` is the command-line interface for **Fabric Backup Lite**. It enables backup and restore of Microsoft Fabric workspaces from the terminal, supporting automation and scripting as a complement to the Windows desktop application.

### What does it do?

It connects to Microsoft Fabric using the official REST API and lets you:

| Command | Description |
|---|---|
| `fbl backup` | Downloads item definitions from a workspace to local folders |
| `fbl restore` | Recreates items from a backup into an existing or new workspace |
| `fbl list workspaces` | Lists all workspaces accessible in the tenant |
| `fbl list items` | Lists the items in a workspace |
| `fbl list backups` | Discovers and lists available backups under a local folder |

### Installation

#### Option A — Download the executable

1. Download `fbl-*-win-x64.zip` from the [**Releases**](https://github.com/nelson-dataxbi/fabric-backup-lite/releases) section of the repository
2. Extract with **PowerShell** (important: do not use Windows Explorer):

```powershell
Expand-Archive fbl-v0.1.1-cli-win-x64.zip -DestinationPath C:\Tools\fbl

# Add to PATH (run once)
[Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";C:\Tools\fbl", "User")
```

3. Verify the installation:

> **Note:** if you extract the zip using Windows Explorer instead of PowerShell, Windows may show a security warning when running `fbl.exe` for the first time. To bypass it: click **"More info"** → **"Run anyway"**. The executable is safe — the full source code is publicly available in this repository.

```
fbl --version
```

> No .NET installation required — the executable is self-contained.

#### Option B — Build from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Git](https://git-scm.com/)

```powershell
git clone https://github.com/wcalcagno/fabric-backup-lite.git
cd fabric-backup-lite
.\installer\PublishCli.ps1
# Output: dist\cli\fbl.exe
```

### Authentication

`fbl` supports two authentication modes:

| Mode | Flag | Use case |
|---|---|---|
| `az login` (default) | `--auth az-login` | Interactive use — requires [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) installed and `az login` already run |
| Service Principal | `--auth sp` | Automation, CI/CD, unattended execution |

#### az-login mode (interactive)

```powershell
az login
fbl list workspaces
```

If your account has MFA enabled, the browser opens for login on first use. Credentials are cached afterwards.

#### Service Principal mode

```powershell
fbl list workspaces --auth sp --client-id <id> --client-secret <secret> --tenant-id <tenant>
```

You can also set environment variables to avoid repeating them on every command:

```powershell
$env:FABRIC_AUTH          = "sp"
$env:FABRIC_CLIENT_ID     = "<id>"
$env:FABRIC_CLIENT_SECRET = "<secret>"
$env:FABRIC_TENANT_ID     = "<tenant>"

fbl list workspaces
```

##### Required permissions for Service Principal

The App Registration in Microsoft Entra ID needs these **application** permissions on the Power BI Service API:

| Permission | Required for |
|---|---|
| `Workspace.Read.All` | List workspaces and items, backup |
| `Item.ReadAll` | Read item definitions (backup) |
| `Workspace.ReadWrite.All` | Create workspaces (restore with `--new-workspace`) |
| `Item.ReadWrite.All` | Create items (restore) |

---

### Commands

#### Global authentication options

Available on all commands:

| Option | Description | Default |
|---|---|---|
| `--auth` | Authentication mode: `az-login` or `sp` | `az-login` |
| `--client-id` | Service principal client ID | — |
| `--client-secret` | Service principal client secret | — |
| `--tenant-id` | Directory tenant ID | — |

---

#### `fbl list workspaces`

Lists all workspaces accessible in the tenant.

```
fbl list workspaces [--output table|json]
```

```
ID                                      Name
--------------------------------------------------------------------------------
0e1404ef-2576-49d0-b3b2-7cc63c3665a2    Taller_Fabric_2026-03_00
...

309 workspace(s) found.
```

---

#### `fbl list items`

Lists all items in a workspace.

```
fbl list items --workspace <name-or-id> [--output table|json]
```

| Option | Required | Description |
|---|---|---|
| `--workspace` | Yes | Workspace name or GUID |
| `--output` | No | `table` (default) or `json` |

```
Type                    Name
--------------------------------------------------------------------------------
DataPipeline            PL_ETL
Notebook                NB_INGST_TasasCambio
SemanticModel           DW-Ana
...

32 item(s) in workspace 'Taller_Fabric_2026-03_00'.
```

---

#### `fbl list backups`

Recursively discovers all backups under a root folder and lists them sorted newest-first, with a numeric index usable in `fbl restore`.

```
fbl list backups --root <path> [--output table|json]
```

| Option | Required | Description |
|---|---|---|
| `--root` | Yes | Root folder to search for backups |
| `--output` | No | `table` (default) or `json` |

```
#    Workspace                       Date                 Items
--------------------------------------------------------------------
1    Taller_Fabric_2026-03_00        2026-03-20 14:55:19     16
2    Taller_Fabric_2026-03_00        2026-03-19 09:12:44     16
3    dataXbi                         2026-03-18 17:30:01      8

3 backup(s) found.
```

> No authentication required — reads local files only.

---

#### `fbl backup`

Backs up a complete workspace or all accessible workspaces.

```
fbl backup --workspace <name-or-id> --dest <path> [--item-types <types>]
fbl backup --all --dest <path> [--item-types <types>]
```

| Option | Required | Description |
|---|---|---|
| `--workspace` | Yes (or `--all`) | Workspace name or GUID |
| `--all` | Yes (or `--workspace`) | Backs up all accessible workspaces into separate subfolders |
| `--dest` | Yes | Destination folder |
| `--item-types` | No | Comma-separated filter: `Notebook`, `SemanticModel`, `Report`, `DataPipeline`, `Dataflow`, `Lakehouse`, `Warehouse`, `KQLDatabase`, `Eventhouse`, `Environment`, `SparkJobDefinition` |

**Exit codes:** `0` success · `1` partial failure (some items failed) · `2` fatal error

**Examples:**

```powershell
# Full workspace backup
fbl backup --workspace Taller_Fabric_2026-03_00 --dest backups/

# Only notebooks and semantic models
fbl backup --workspace Taller_Fabric_2026-03_00 --dest backups/ --item-types Notebook,SemanticModel

# Back up all workspaces
fbl backup --all --dest backups/
```

**Folder structure on disk:**

```
backups/
└── <WorkspaceName>/
    └── <TenantId>/
        └── <WorkspaceName>_<WorkspaceId>/
            └── <UnixTimestampMs>_backup/
                ├── Notebooks/
                ├── SemanticModels/
                ├── DataPipelines/
                └── manifest.json
```

---

#### `fbl restore`

Restores items from a local backup to a Fabric workspace.

```
fbl restore --source <path>          --workspace <name-or-id>
fbl restore --source <path>          --new-workspace <name> --capacity <id>
fbl restore --root <path> --backup <n|latest>  --workspace <name-or-id>
fbl restore --root <path> --backup <n|latest>  --new-workspace <name> --capacity <id>
```

| Option | Required | Description |
|---|---|---|
| `--source` | Yes (or `--root`+`--backup`) | Direct path to the backup folder (the one containing `manifest.json`) |
| `--root` | Yes (with `--backup`) | Root folder to discover backups — same as in `fbl list backups` |
| `--backup` | Yes (with `--root`) | Backup index from `fbl list backups`, or `latest` for the most recent |
| `--workspace` | Yes (or `--new-workspace`) | Target workspace (name or GUID) |
| `--new-workspace` | Yes (or `--workspace`) | Name of a new workspace to create |
| `--capacity` | With `--new-workspace` | Fabric capacity ID for the new workspace |
| `--item-types` | No | Filter which item types to restore |

**Exit codes:** `0` success · `1` partial failure · `2` fatal error

**Notes:**
- `--source` and `--root`/`--backup` are mutually exclusive
- Warehouses are skipped with a warning — they cannot be restored via API
- If an item already exists in the target workspace it is logged as a warning and processing continues

**Examples:**

```powershell
# Restore the latest backup to the original workspace
fbl restore --root backups/ --backup latest --workspace Taller_Fabric_2026-03_00

# Restore the second most recent backup to a different workspace
fbl restore --root backups/ --backup 2 --workspace Taller_Fabric_2026-03_00_R

# Restore to a brand-new workspace
fbl restore --root backups/ --backup latest --new-workspace MyNewWorkspace --capacity <capacity-id>

# Restore from a direct path
fbl restore --source "backups/Taller_Fabric_2026-03_00/.../1774018452734_backup" --workspace Taller_Fabric_2026-03_00_R

# Restore notebooks only
fbl restore --root backups/ --backup latest --workspace Taller_Fabric_2026-03_00_R --item-types Notebook
```

---

### Typical workflow

```powershell
# 1. Back up a workspace
fbl backup --workspace MyWorkspace --dest backups/

# 2. List available backups
fbl list backups --root backups/

# 3. Restore the latest backup to a test workspace
fbl restore --root backups/ --backup latest --workspace MyWorkspace_Test
```
