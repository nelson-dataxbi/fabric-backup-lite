# GitHub Copilot Instructions — Fabric Backup Lite

## Scope

These instructions apply **only to the CLI project** (`src/Fabric_backup_lite.Cli/`) and the shared Core library (`src/Fabric_backup_lite.Core/`).

> **⚠️ Do NOT modify `src/Fabric_backup_lite/` (the WPF application).** That project is stable and must remain untouched. Any change to it is out of scope.

---

## Project overview

**Fabric Backup Lite** is a Windows desktop app (WPF/.NET 8) for backing up and restoring Microsoft Fabric and Power BI artifacts. We are adding a CLI companion tool (`fbl`) that provides the same functionality from the command line.

The solution will contain three projects:

| Project | Type | Description |
|---|---|---|
| `Fabric_backup_lite` | WPF app | Existing app — **do not modify** |
| `Fabric_backup_lite.Core` | Class library | Shared services and models (copied from WPF, CLI only) |
| `Fabric_backup_lite.Cli` | Console app | CLI entry point — new work lives here |

See `src/Fabric_backup_lite.Cli/copilot-instructions.md` for detailed CLI specifications.

---

## General conventions

- **Language:** C# 12, `.NET 8.0-windows`
- **Nullable:** `<Nullable>enable</Nullable>` — always respect nullable annotations
- **Implicit usings:** enabled
- **Logging:** Serilog (console sink for CLI output, file sink for diagnostics)
- **DI:** `Microsoft.Extensions.DependencyInjection`
- **Async:** all I/O operations must be `async/await` with `CancellationToken` support
- **Comments:** only where logic needs clarification; no redundant comments

## Commit convention

All Copilot-generated commits must use:
```
git commit --author="Copilot <223556219+Copilot@users.noreply.github.com>"
```
