# Eflay Game Save Manager

Game save manager for keeping local game save paths and cloud backups in one shared configuration.

The repository currently contains multiple front ends and helper projects. The active direction is the lightweight Lazarus UI for Winlator.

## Projects

### `EflayGameSaveManager.Core`

Shared business logic.

- Reads and writes `GameSaveManager.config.json`.
- Resolves current-device paths from `GameSaveManager.runtime.json`.
- Resolves path tokens such as `<winDocuments>`, `<winLocalAppData>`, and `<home>`.
- Creates local save archives.
- Uploads, lists, downloads, restores, and deletes cloud backups through S3-compatible storage.

Most UI projects should reuse this project instead of duplicating save/archive/cloud behavior.

### `EflayGameSaveManager.Avalonia`

Main desktop UI built with Avalonia.

- Full-featured .NET desktop front end.
- Good for normal Windows desktop use.
- Starts quickly on normal Windows desktop.
- Can run in Winlator and GameSir environments, but startup is a little slower there.
- Uses the shared Core project for config, save path handling, and cloud sync.

Build:

```powershell
dotnet build src\EflayGameSaveManager.Avalonia\EflayGameSaveManager.Avalonia.csproj -c Release
```

Native AOT publish script:

```powershell
src\EflayGameSaveManager.Avalonia\publishaot.bat
```

### `EflayGameSaveManager.Lazarus`

Lightweight Lazarus/FreePascal UI intended for Winlator.

- Starts faster than the Avalonia UI.
- Reads the existing `GameSaveManager.config.json`.
- Lists games and current-device save paths.
- Edits current-device save path and game executable path.
- Provides local backup, run game, and open folder actions.
- Prioritizes cloud operations with visible buttons for status, upload, and restore.
- Implements cloud status/upload/restore directly in Lazarus (S3-compatible requests and signing).
- Uses external 7-Zip for cloud restore extraction:
  - `7zz.exe` (recommended), or
  - `7za.exe`, or
  - `7z.exe` + `7z.dll`.
- Supports Lite-only config file `GameSaveManagerLite.config.json`:
  - `settings.forced_device_name` for Lite current-device override (higher priority than runtime file).
  - `settings.seven_zip_dir` to override the 7-Zip directory.
  - If omitted, 7-Zip is resolved from the same directory as `GameSaveManagerLite.exe`.

The deployable files are expected to sit together in:

```text
src\EflayGameSaveManager.Lazarus\bin
```

Build:

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

### `EflayGameSaveManager.CloudTool`

Small command-line helper for scripting and diagnostics.

- Reuses `EflayGameSaveManager.Core`.
- Keeps S3 signing, archive creation, and restore behavior in one .NET implementation.
- Supports:
  - `status`
  - `upload-current`
  - `restore-current`

Example:

```powershell
src\EflayGameSaveManager.Lazarus\bin\GameSaveManager.CloudTool.exe --action status --config GameSaveManager.config.json --game WRCG
```

Native AOT publish script:

```powershell
src\EflayGameSaveManager.CloudTool\publishaot.bat
```

### `EflayGameSaveManager.Lvgl`

Experimental LVGLSharp front end.

This version has not produced a usable result yet and is still in the trial stage. It is kept in the solution for exploration, but it should not be treated as a stable or recommended UI at the moment.

Current notes:

- Uses `LVGLSharp.Forms` and `LVGLSharp.Runtime.Windows`.
- Attempts a paged game list and basic cloud sync/restore actions.
- Not the current Winlator delivery target.

### `EflayGameSaveManager.Core.Tests`

Unit tests for Core behavior.

Build/test:

```powershell
dotnet test tests\EflayGameSaveManager.Core.Tests\EflayGameSaveManager.Core.Tests.csproj -c Release
```

## Configuration Files

The app expects these files near the working directory or a parent directory:

- `GameSaveManager.config.json`: game definitions, save paths, cloud backend, devices.
- `GameSaveManager.runtime.json`: local runtime settings, currently used for `forced_device_name`.
- `GameSaveManagerLite.config.json` (Lazarus optional): Lite-only overrides such as `settings.forced_device_name` and `settings.seven_zip_dir`.

The cloud backend is configured under `settings.cloud_settings` in `GameSaveManager.config.json`.

The configuration file format is based on the upstream [`mcthesw/game-save-manager`](https://github.com/mcthesw/game-save-manager) project.

## Current Recommended Workflow

For normal desktop Windows usage, use the Avalonia project.

For Winlator usage, use the Lazarus Lite UI. Place a supported 7-Zip executable in `src\EflayGameSaveManager.Lazarus\bin` (or configure `settings.seven_zip_dir` in `GameSaveManagerLite.config.json`):

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

Then run:

```text
src\EflayGameSaveManager.Lazarus\bin\GameSaveManagerLite.exe
```

---

# Eflay Game Save Manager 中文说明

这是一个游戏存档管理工具，用同一份配置维护本地游戏存档路径和云端备份。
当前仓库包含多个前端与辅助项目，现阶段面向 Winlator 的主力是 Lazarus 轻量版。

## 项目说明

### `EflayGameSaveManager.Core`

共享业务逻辑：
- 读写 `GameSaveManager.config.json`
- 解析 `GameSaveManager.runtime.json`
- 解析 `<winDocuments>`、`<winLocalAppData>`、`<home>` 等路径 token
- 创建本地存档压缩包
- 通过 S3 兼容存储进行上传、列表、下载、恢复、删除

### `EflayGameSaveManager.Avalonia`

主力 .NET 桌面版（Windows 常规桌面优先使用）。

构建：
```powershell
dotnet build src\EflayGameSaveManager.Avalonia\EflayGameSaveManager.Avalonia.csproj -c Release
```

### `EflayGameSaveManager.Lazarus`

面向 Winlator 的轻量 Lazarus/FreePascal 界面。

当前状态：
- 支持本地路径编辑、备份、运行游戏、打开目录
- 云状态/上传/恢复已在 Lazarus 内置实现（不再依赖 CloudTool）
- 云恢复解压依赖外部 7-Zip

7-Zip 支持顺序：
- `7zz.exe`（推荐）
- `7za.exe`
- `7z.exe` + `7z.dll`（同目录且位数匹配）

Lite 独立配置文件：
- 文件名：`GameSaveManagerLite.config.json`
- 配置项：
  - `settings.forced_device_name`：Lazarus 当前设备名覆盖
  - `settings.seven_zip_dir`：7-Zip 所在目录（可选）

示例：
```json
{
  "settings": {
    "forced_device_name": "MyWinlatorDevice",
    "seven_zip_dir": ".\\tools\\7zip"
  }
}
```

构建：
```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

### `EflayGameSaveManager.CloudTool`

命令行工具，主要用于脚本化操作和排障（Lazarus 日常云流程不再依赖它）。

## 配置文件

程序会在当前目录或父目录查找：
- `GameSaveManager.config.json`：游戏定义、存档路径、云端后端、设备信息
- `GameSaveManager.runtime.json`：运行时设置（如 `forced_device_name`）
- `GameSaveManagerLite.config.json`（Lazarus 可选）：Lite 专用覆盖项（`forced_device_name`、`seven_zip_dir`）

## 推荐用法

- 常规 Windows 桌面：使用 Avalonia 版本
- Winlator：使用 Lazarus 版本，并确保可用的 7-Zip 可执行文件在 `bin` 目录或 `settings.seven_zip_dir` 指定目录

运行：
```text
src\EflayGameSaveManager.Lazarus\bin\GameSaveManagerLite.exe
```
