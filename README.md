# Eflay Game Save Manager

Game save manager for keeping local game save paths and cloud backups in one shared configuration.

The repository currently contains multiple front ends and helper projects. The active direction is the lightweight Lazarus UI for Winlator, with cloud work delegated to the existing .NET Core implementation.

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
- Delegates cloud work to `GameSaveManager.CloudTool.exe`.

The deployable files are expected to sit together in:

```text
src\EflayGameSaveManager.Lazarus\bin
```

Build:

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

### `EflayGameSaveManager.CloudTool`

Small command-line helper used by the Lazarus UI for cloud operations.

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

The script publishes into the Lazarus `bin` directory so the Lite UI can find it.

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

The cloud backend is configured under `settings.cloud_settings` in `GameSaveManager.config.json`.

The configuration file format is based on the upstream [`mcthesw/game-save-manager`](https://github.com/mcthesw/game-save-manager) project.

## Current Recommended Workflow

For normal desktop Windows usage, use the Avalonia project.

For Winlator usage, use the Lazarus Lite UI and publish the CloudTool into the same `bin` directory:

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
src\EflayGameSaveManager.CloudTool\publishaot.bat
```

Then run:

```text
src\EflayGameSaveManager.Lazarus\bin\GameSaveManagerLite.exe
```

---

# Eflay Game Save Manager 中文说明

这是一个游戏存档管理工具，用同一份配置维护本地游戏存档路径和云端备份。

当前仓库里有多个前端和辅助项目。现阶段的主要方向是面向 Winlator 的轻量 Lazarus 界面，云端同步能力委托给已有的 .NET Core 实现。

## 项目说明

### `EflayGameSaveManager.Core`

共享业务逻辑。

- 读取和写入 `GameSaveManager.config.json`。
- 根据 `GameSaveManager.runtime.json` 解析当前设备。
- 解析 `<winDocuments>`、`<winLocalAppData>`、`<home>` 等路径 token。
- 创建本地存档压缩包。
- 通过 S3 兼容存储上传、列出、下载、恢复、删除云端备份。

各个 UI 项目应尽量复用这个项目，不要重复实现存档、压缩包和云同步逻辑。

### `EflayGameSaveManager.Avalonia`

Avalonia 桌面主界面。

- 功能较完整的 .NET 桌面前端。
- 适合普通 Windows 桌面使用。
- 在普通 Windows 桌面下启动很快。
- 可以运行在 Winlator 和盖世游戏环境中，但启动会稍慢一些。
- 使用 Core 项目处理配置、存档路径和云同步。

构建：

```powershell
dotnet build src\EflayGameSaveManager.Avalonia\EflayGameSaveManager.Avalonia.csproj -c Release
```

Native AOT 发布脚本：

```powershell
src\EflayGameSaveManager.Avalonia\publishaot.bat
```

### `EflayGameSaveManager.Lazarus`

面向 Winlator 的轻量 Lazarus/FreePascal 界面。

- 比 Avalonia 界面启动更轻。
- 读取现有 `GameSaveManager.config.json`。
- 显示游戏列表和当前设备的存档路径。
- 编辑当前设备的存档路径和游戏可执行文件路径。
- 提供本地备份、运行游戏、打开目录等操作。
- 云端操作按钮更显眼，包含状态、上传、恢复。
- 云同步委托给 `GameSaveManager.CloudTool.exe`。

可部署文件应放在同一个目录：

```text
src\EflayGameSaveManager.Lazarus\bin
```

构建：

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

### `EflayGameSaveManager.CloudTool`

供 Lazarus Lite 界面调用的云同步命令行工具。

- 复用 `EflayGameSaveManager.Core`。
- S3 签名、压缩包创建、恢复逻辑都保留在同一套 .NET 实现里。
- 支持：
  - `status`
  - `upload-current`
  - `restore-current`

示例：

```powershell
src\EflayGameSaveManager.Lazarus\bin\GameSaveManager.CloudTool.exe --action status --config GameSaveManager.config.json --game WRCG
```

Native AOT 发布脚本：

```powershell
src\EflayGameSaveManager.CloudTool\publishaot.bat
```

该脚本会把 CloudTool 发布到 Lazarus 的 `bin` 目录，方便 Lite 界面直接找到它。

### `EflayGameSaveManager.Lvgl`

实验性的 LVGLSharp 前端。

这个版本目前还没有形成可用成果，仍处于尝试阶段。它保留在解决方案中用于探索，但现在不应当视为稳定或推荐的 UI。

当前情况：

- 使用 `LVGLSharp.Forms` 和 `LVGLSharp.Runtime.Windows`。
- 尝试了分页游戏列表和基础云同步/恢复动作。
- 不是当前 Winlator 交付目标。

### `EflayGameSaveManager.Core.Tests`

Core 行为的单元测试项目。

测试：

```powershell
dotnet test tests\EflayGameSaveManager.Core.Tests\EflayGameSaveManager.Core.Tests.csproj -c Release
```

## 配置文件

程序会在当前工作目录或其父目录中查找：

- `GameSaveManager.config.json`：游戏定义、存档路径、云端后端、设备信息。
- `GameSaveManager.runtime.json`：本机运行时设置，目前用于 `forced_device_name`。

云端后端配置位于 `GameSaveManager.config.json` 的 `settings.cloud_settings` 下。

配置文件格式来源于上游项目 [`mcthesw/game-save-manager`](https://github.com/mcthesw/game-save-manager)。

## 当前推荐用法

普通 Windows 桌面使用 Avalonia 项目。

Winlator 使用 Lazarus Lite 界面，并把 CloudTool AOT 发布到同一个 `bin` 目录：

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
src\EflayGameSaveManager.CloudTool\publishaot.bat
```

然后运行：

```text
src\EflayGameSaveManager.Lazarus\bin\GameSaveManagerLite.exe
```
