# Especificación CLI — Fabric Backup Lite

## Autor y contexto

Esta especificación fue diseñada por **Nelson López Centeno** como contribución al proyecto Fabric Backup Lite (desarrollado originalmente por Walter Calcagno Lucares).

El objetivo es agregar una interfaz de línea de comandos (`fbl`) que exponga la funcionalidad de backup y restore desde la terminal, habilitando automatización y flujos de trabajo en scripts que que complementen la aplicación de escritorio Windows existente.

---

## ¿Por qué una CLI?

### Análisis de gaps: Fabric CLI vs. Fabric Backup Lite

[Fabric CLI oficial de Microsoft](https://learn.microsoft.com/en-us/fabric/cicd/fabrics-cli) (`fab`) puede exportar items individuales, mientras que la CLI de este proyecto agregaría **orquestación y operaciones masivas** sobre la Fabric REST API, que es el valor diferencial de Fabric Backup Lite frente a la CLI oficial.

| Funcionalidad | Fabric CLI | Fabric Backup Lite |
|---|---|---|
| Backup masivo de un workspace completo | ❌ | ✅ |
| Auto-descubrimiento de todos los items de un workspace | ❌ | ✅ |
| Manifest de backup con timestamp y metadata | ❌ | ✅ |
| Restore orquestado (item por item, con log) | ❌ | ✅ |
| Crear workspace destino durante el restore | ❌ | ✅ |
| Backup de Warehouse vía OneLake ADLS | ❌ | ✅ |



---

## Decisiones de diseño

### 1. Sin cambios al proyecto WPF existente

El proyecto WPF `Fabric_backup_lite` existente es estable y no debe modificarse. Se creará una nueva librería compartida (`Fabric_backup_lite.Core`) copiando los servicios y modelos, y será utilizada exclusivamente por la CLI.

### 2. Dos modos de autenticación

La CLI soportará dos modos de autenticación para distintos casos de uso:

| Modo | Flag | Caso de uso |
|---|---|---|
| `az login` | `--auth az-login` (por defecto) | Uso interactivo — sin configuración adicional más allá de `az login` |
| Service Principal | `--auth sp` | Pipelines de CI/CD, automatización, ejecución desatendida |

**¿Por qué `az login` como valor por defecto?**
Los usuarios de CLI generalmente ya tienen la Azure CLI instalada. Sin configuración adicional (sin necesidad de App Registration), `az login` es la opción de menor fricción para uso interactivo.

**¿Por qué Service Principal para CI/CD?**
Los service principals permiten ejecución desatendida y no interactiva. Todos los endpoints de la Fabric REST API utilizados por este proyecto soportan autenticación con service principal (verificado contra la documentación oficial de Microsoft).

Ambos modos de auth cubrirán toda la funcionalidad — ninguna operación quedará bloqueada al usar service principal.

### 3. Distribución como EXE self-contained

La CLI se distribuirá como `fbl.exe` (self-contained, sin necesidad de tener .NET runtime instalado). Será más simple que un instalador MSI y apropiado para una herramienta dirigida a desarrolladores y administradores cómodos gestionando su propio PATH. El instalador MSI existente de la app WPF no se modificará.

### 4. `System.CommandLine` como framework de CLI

Framework oficial de .NET para CLIs de Microsoft. Proveerá parsing de argumentos, generación de ayuda y soporte de subcomandos de forma nativa. Incluirá `--help` / `-h` automático en cada comando y subcomando, y `--version` en el comando raíz.

### 5. Versionado independiente de la CLI con MinVer

La CLI tendrá su propia versión, independiente de la app WPF. Se usará **MinVer** — un NuGet package que calcula la versión automáticamente a partir de los **git tags**, sin configuración adicional.

Flujo de release:
1. Desarrollar y hacer commits normalmente
2. Al momento del release: `git tag v1.0.0` (o `v1.1.0`, `v2.0.0`, etc.)
3. `dotnet publish` genera `fbl.exe` con esa versión embebida automáticamente
4. `fbl --version` devuelve el número de versión correcto
5. Subir `fbl.exe` a GitHub Releases como asset

Entre tags, MinVer genera versiones pre-release automáticamente (ej. `1.0.1-alpha.5`), lo que permite distinguir builds de desarrollo de releases oficiales. El proyecto WPF no se ve afectado — MinVer solo se agrega al proyecto CLI.

### 6. `--workspace` acepta nombre o GUID

Para evitar que el usuario tenga que buscar IDs de workspace, `--workspace` aceptará tanto el nombre como el GUID. Si un nombre coincide con más de un workspace, la CLI terminará con error listando los IDs coincidentes para que el usuario pueda reintentar con el ID específico.

---

## Estructura de la solución

```
fabric-backup-lite/
├── specs/
│   └── cli.md                       ← este archivo
├── .github/
│   └── copilot-instructions.md      ← instrucciones de Copilot a nivel repositorio (scope CLI)
├── installer/
│   └── PublishCli.ps1               ← script para generar fbl.exe
└── src/
    ├── Fabric_backup_lite/          ← app WPF (sin cambios)
    ├── Fabric_backup_lite.Core/     ← librería compartida (Models + Services, solo para CLI)
    └── Fabric_backup_lite.Cli/
        ├── copilot-instructions.md  ← spec detallada de implementación para Copilot
        └── ...
```

---

## Comandos

### Opciones globales de autenticación

| Opción | Descripción | Por defecto |
|---|---|---|
| `--auth` | `az-login` o `sp` | `az-login` |
| `--client-id` | Client ID del service principal | — |
| `--client-secret` | Client secret del service principal | — |
| `--tenant-id` | Tenant ID | — |

La autenticación también se podrá proveer mediante variables de entorno: `FABRIC_AUTH`, `FABRIC_CLIENT_ID`, `FABRIC_CLIENT_SECRET`, `FABRIC_TENANT_ID`.

---

### `fbl backup`

```
fbl backup --workspace <nombre-o-id> --dest <ruta> [--item-types <tipos>]
fbl backup --all --dest <ruta> [--item-types <tipos>]
```

| Opción | Requerida | Descripción |
|---|---|---|
| `--workspace` | Sí (o `--all`) | Nombre o GUID del workspace |
| `--all` | Sí (o `--workspace`) | Hace backup de todos los workspaces accesibles en subcarpetas separadas dentro de `--dest` |
| `--dest` | Sí | Carpeta destino |
| `--item-types` | No | Filtro separado por comas. Valores: `Report`, `SemanticModel`, `Notebook`, `DataPipeline`, `Dataflow`, `Lakehouse`, `Warehouse`, `KQLDatabase`, `Eventhouse`, `Environment`, `SparkJobDefinition` |

**Exit codes:** `0` éxito · `1` falla parcial · `2` error fatal

---

### `fbl list workspaces`

```
fbl list workspaces [--output table|json]
```

---

### `fbl list items`

```
fbl list items --workspace <nombre-o-id> [--output table|json]
```

---

### `fbl list backups`

```
fbl list backups --root <ruta> [--output table|json]
```

Descubrirá recursivamente todos los `manifest.json` bajo `--root` y los mostrará ordenados por fecha descendente (el más reciente primero), con un índice numérico utilizable en `fbl restore`.

| Opción | Requerida | Descripción |
|---|---|---|
| `--root` | Sí | Carpeta raíz donde buscar backups |
| `--output` | No | `table` (por defecto) o `json` |

Salida en modo tabla:

```
#   Workspace                  Fecha                Items
--  -------------------------  -------------------  -----
1   Taller_Fabric_2026-03_00   2026-03-20 14:54     16
2   Taller_Fabric_2026-03_00   2026-03-19 09:12     16
3   dataXbi                    2026-03-18 17:30       8
```

---

### `fbl restore`

```
fbl restore --source <ruta> --workspace <nombre-o-id>
fbl restore --source <ruta> --new-workspace <nombre> --capacity <id>
fbl restore --root <ruta> --backup <n|latest> --workspace <nombre-o-id>
fbl restore --root <ruta> --backup <n|latest> --new-workspace <nombre> --capacity <id>
```

| Opción | Requerida | Descripción |
|---|---|---|
| `--source` | Sí (o `--root`+`--backup`) | Ruta directa a la carpeta de backup que contiene `manifest.json` |
| `--root` | Sí (con `--backup`) | Carpeta raíz donde buscar backups (misma que `fbl list backups --root`) |
| `--backup` | Sí (con `--root`) | Índice numérico del backup (del listado de `fbl list backups`) o `latest` para el más reciente |
| `--workspace` | Sí (o `--new-workspace`) | Nombre o GUID del workspace destino |
| `--new-workspace` | Sí (o `--workspace`) | Nombre para crear un nuevo workspace |
| `--capacity` | Con `--new-workspace` | ID de la capacity de Fabric |
| `--item-types` | No | Filtra los tipos a restaurar |

**Notas:**
- `--source` y `--root`/`--backup` son mutuamente excluyentes
- `--backup latest` selecciona el backup más reciente encontrado bajo `--root`
- `--backup 1` selecciona el primer resultado del listado de `fbl list backups --root <ruta>` (orden descendente por fecha)
- Los Warehouses se omiten con advertencia (no son restaurables vía API)
- 409 Conflict (el item ya existe) → se registra como advertencia y continúa con los items restantes

---

## Compatibilidad con la API

Todos los endpoints de la Fabric REST API utilizados por este proyecto soportan autenticación con service principal (verificado en marzo de 2026 contra la [documentación de Microsoft Fabric REST API](https://learn.microsoft.com/rest/api/fabric/articles/item-management/item-management-overview)):

| Tipo de item | Soporte SP |
|---|---|
| Notebook, DataPipeline, Environment, Lakehouse, SparkJobDefinition | ✅ |
| Report, SemanticModel, Dataflow | ✅ |
| KQLDatabase, Eventhouse | ✅ |
| Warehouse | ✅ (backup vía OneLake ADLS; restore no soportado vía API) |
