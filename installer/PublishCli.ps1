<#
.SYNOPSIS
    Publica fbl.exe como ejecutable único autocontenido para Windows x64.

.PARAMETER Runtime
    RID de destino. Por defecto: win-x64.

.PARAMETER OutDir
    Carpeta de salida. Por defecto: dist\cli

.EXAMPLE
    .\PublishCli.ps1
    .\PublishCli.ps1 -OutDir C:\temp\fbl
#>
param(
    [string]$Runtime = "win-x64",
    [string]$OutDir  = "$PSScriptRoot\..\dist\cli"
)

$root    = Resolve-Path "$PSScriptRoot\.."
$project = "$root\src\Fabric_backup_lite.Cli\Fabric_backup_lite.Cli.csproj"

Write-Host "Publishing fbl -> $OutDir" -ForegroundColor Cyan

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $OutDir "fbl.exe"
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
