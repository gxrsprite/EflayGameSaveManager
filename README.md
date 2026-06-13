# Eflay Game Save Manager

Game save manager for keeping local game save paths and cloud backups in one shared configuration.

The recommended app is now the Avalonia desktop version. It can be used independently for normal Windows usage, including creating a new configuration from scratch, adding games, editing save paths, and syncing cloud backups. The Lazarus version is kept as a lightweight fallback to try when Avalonia cannot run in a constrained environment such as some Winlator setups.

## Projects

### `EflayGameSaveManager.Core`

Shared business logic.

- Reads and writes `GameSaveManager.config.json`.
- Resolves current-device paths from `GameSaveManager.runtime.json`.
- Resolves path tokens such as `<winDocuments>`, `<winLocalAppData>`, and `<home>`.
- Creates local save archives.
- Uploads, lists, downloads, restores, and deletes cloud backups through S3-compatible storage.

Most UI projects should reuse this project instead of duplicating save/archive/cloud behavior.

Archive layout note:

- Desktop archives created by Core/Avalonia write a basic RGSM v2 zip comment: `RGSM_ARCHIVE_V2` plus JSON metadata using `compression:"deflate"`.
- Non-zip save units are packed under their stable unit-id directory. `Folder` save units also include the selected folder itself, so a path ending in `SAVEDATA` is stored as `0/SAVEDATA/...` for save unit `0`.
- Restore remains backward-compatible with older flat or no-comment archives and automatically unwraps a single same-named folder, so restoring `SAVEDATA/...` or `0/SAVEDATA/...` to a target `SAVEDATA` path does not create `SAVEDATA/SAVEDATA/...`.

### `EflayGameSaveManager.Avalonia`

Main and recommended desktop UI built with Avalonia.

- Standalone .NET desktop front end for normal Windows use.
- Can start without an existing `GameSaveManager.config.json`; adding a game writes a new config file.
- Lists games, save units, current-device paths, executable paths, cloud status, and cloud backup history.
- Adds new games from the UI.
- Supports folder saves, file saves, zip saves (`Zip`), and Windows registry saves (`WinRegistry`).
- Lets users select file/folder paths or edit paths manually, including token paths such as `<home>` and `<winLocalAppData>`.
- Provides a Settings page for editing the S3-compatible cloud backend.
- Creates local backups (zip archives with `Backups.json` manifests).
- Lists local backups with device name, size, and date; restores or deletes selected local backups.
- Uploads current saves to S3-compatible cloud storage.
- Restores current cloud saves and selected cloud backup zip entries.
- Downloads, deletes, and rebuilds cloud backup indexes.
- Can run the configured game executable and open related folders.
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

Lightweight Lazarus/FreePascal fallback UI.

- Intended as a fallback when the Avalonia app cannot run or starts too slowly in a constrained environment.
- Reads the existing `GameSaveManager.config.json`.
- Lists games and current-device save paths.
- Edits current-device save path and game executable path.
- Provides local backup, run game, and open folder actions.
- Supports folder, file, and Windows registry save units.
- Provides cloud status, upload, restore, and manifest rebuild actions.
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

### `EflayGameSaveManager.Maui`

.NET MAUI Android 客户端，共享 `EflayGameSaveManager.Core` 业务逻辑。

当前功能：
- 复用 Core 的配置管理、路径解析和 S3 云端同步
- 游戏列表 + 收藏栏，查看云状态，上传/恢复当前存档
- SAF 文件/文件夹选择器，支持 `content://` → 真实路径解析
- 首次启动从 assets 种子 `GameSaveManager.config.json` + `GameSaveManager.runtime.json`
- 启动时配置标准化（合并重复存档单元、旧 config 清理）
- `MANAGE_EXTERNAL_STORAGE` 权限请求（API 30+），cleartext HTTP
- **Shizuku 支持**：通过 JNI 桥接 Shizuku API，为 `/Android/data/` 等受限路径提供特权文件访问。上传时自动将受限文件暂存到 temp，恢复后写回。全部逻辑在 `#if ANDROID` ViewModel 层，Core 保持桌面纯净

构建 & 部署：
```powershell
dotnet build src\EflayGameSaveManager.Maui\EflayGameSaveManager.Maui.csproj -c Debug -f net10.0-android
adb install -r bin\Debug\net10.0-android\com.eflay.gamesavemanager.maui-Signed.apk
```

### `EflayGameSaveManager.Kotlin`

原生 Kotlin / Jetpack Compose Android 客户端，独立实现不依赖 Core。

当前功能：
- 完整复刻 MAUI 版的游戏存档管理流程
- `kotlinx.serialization` 数据模型，`@SerialName` 匹配 C# snake_case JSON
- S3 兼容云存储 + AWS4-HMAC-SHA256 签名（OkHttp）
- zip 归档/恢复，兼容 C# `ArchiveTransferService` 格式
- 收藏（SharedPreferences）、启动配置标准化、设备 ID 解析
- SAF 选择器 → `StoragePathResolver` 解析为真实路径
- `MANAGE_EXTERNAL_STORAGE` + cleartext HTTP
- **Shizuku 支持**：零外部依赖的反射桥接，`ArchiveService` 内建受限路径自动接管。Header 栏彩色指示器显示 Shizuku 状态（未安装 / 无权限 / ADB / root）

构建 & 部署：
```bash
export ANDROID_HOME="$HOME/AppData/Local/Android/Sdk"
export JAVA_HOME="F:/Program Files/Android/Android Studio/jbr"
cd src/EflayGameSaveManager.Kotlin
./gradlew assembleDebug
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

APK ~17MB，增量编译 ~30s。用 Android Studio 打开 `src/EflayGameSaveManager.Kotlin` 目录即可 IDE 开发。

### `EflayGameSaveManager.MewUI`

Lightweight desktop UI built with MewUI.

Current scope:

- Reuses `EflayGameSaveManager.Core` for configuration loading, save snapshot resolution, and S3-compatible cloud sync.
- Lists configured games and shows current-device save paths, executable path, and basic cloud status.
- Supports opening save/game folders, running the configured game executable, uploading current save to cloud, and restoring the current cloud save.
- Supports a basic "quick add game" flow for adding a folder-save game with one current-device save path and an optional executable path.
- Can start without an existing `GameSaveManager.config.json`; adding the first game writes a new config file.

Build:

```powershell
dotnet build src\EflayGameSaveManager.MewUI\EflayGameSaveManager.MewUI.csproj
```

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

Save units support `Folder`, `File`, `Zip`, and Windows registry keys via `WinRegistry`.

The cloud backend is configured under `settings.cloud_settings` in `GameSaveManager.config.json`.
The Avalonia app can edit the S3 endpoint, bucket, region, root path, access key, secret key, and sync options from `Settings`.

## Zip Mode

Use `Zip` save units when the complete save state already exists as a single `.zip` file.

- Add a save unit with `unit_type` set to `Zip`.
- Set the current-device path to the `.zip` file.
- Local backup and cloud upload copy that zip directly instead of repacking its contents.
- Restore writes the downloaded or selected backup zip back to the configured zip path.
- Prefer keeping a Zip-mode game to Zip units only; mixing Zip with `Folder`, `File`, or `WinRegistry` units should be reserved for compatibility cases.

The configuration file format is based on the upstream [`mcthesw/game-save-manager`](https://github.com/mcthesw/game-save-manager) project.

For an implementation-oriented guide intended for AI coding assistants and future maintainers, see [`docs/AI_DEVELOPMENT_GUIDE.md`](docs/AI_DEVELOPMENT_GUIDE.md).

## Current Recommended Workflow

Use the Avalonia app first. It is the main version and can create and maintain `GameSaveManager.config.json` on its own.

Run Avalonia after building:

```text
src\EflayGameSaveManager.Avalonia\bin\Release\net10.0\GameSaveManager.exe
```

Use the Lazarus Lite UI only as a fallback when Avalonia cannot run in the target environment. For Lazarus cloud restore, place a supported 7-Zip executable in `src\EflayGameSaveManager.Lazarus\bin` (or configure `settings.seven_zip_dir` in `GameSaveManagerLite.config.json`):

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

Then run the fallback UI:

```text
src\EflayGameSaveManager.Lazarus\bin\GameSaveManagerLite.exe
```

---

# Eflay Game Save Manager 中文说明

这是一个游戏存档管理工具，用同一份配置维护本地游戏存档路径和云端备份。
当前主推版本是 Avalonia 桌面版。它可以独立使用，支持从零创建配置、添加游戏、编辑存档路径和云同步。Lazarus 轻量版保留为备用方案，主要用于 Avalonia 在某些 Winlator 等受限环境中无法运行或启动过慢时尝试。

## 项目说明

### `EflayGameSaveManager.Core`

共享业务逻辑：
- 读写 `GameSaveManager.config.json`
- 解析 `GameSaveManager.runtime.json`
- 解析 `<winDocuments>`、`<winLocalAppData>`、`<home>` 等路径 token
- 创建本地存档压缩包
- 通过 S3 兼容存储进行上传、列表、下载、恢复、删除

归档格式说明：
- Core/Avalonia 创建的桌面压缩包会写入基础 RGSM v2 zip comment：`RGSM_ARCHIVE_V2` 加 JSON 元数据，压缩方式标记为 `compression:"deflate"`。
- 非 Zip 存档单元都会先使用稳定的 unit id 目录；`Folder` 存档单元还会保留所选文件夹本身。例如 unit `0` 的路径末尾是 `SAVEDATA` 时，zip 内保存为 `0/SAVEDATA/...`。
- 恢复时继续兼容旧的扁平布局或无 comment 压缩包，并会自动拆掉一层同名目录，避免把 `SAVEDATA/...` 或 `0/SAVEDATA/...` 恢复成 `SAVEDATA/SAVEDATA/...`。

### `EflayGameSaveManager.Avalonia`

主力 .NET 桌面版（推荐优先使用）。

当前功能：
- 无需预先存在 `GameSaveManager.config.json`，首次添加游戏后会自动生成配置文件
- 查看游戏列表、当前设备、存档路径、游戏路径、云状态和云备份历史
- 在界面中添加新游戏
- 支持文件夹存档、单文件存档、zip 存档（`Zip`）、Windows 注册表存档（`WinRegistry`）
- 路径可通过选择器选择，也可以手动编辑并使用 `<home>`、`<winLocalAppData>` 等 token
- 提供 Settings 页面，可在界面中配置 S3 兼容云存储
- 创建本地备份（zip 压缩包 + Backups.json 清单）
- 查看本地备份列表（显示设备名、大小、日期），恢复或删除选中的本地备份
- 上传当前存档到 S3 兼容云存储
- 从当前云存档或指定云备份恢复
- 下载、删除云备份，重建云备份索引
- 运行配置的游戏可执行文件，打开游戏目录或存档目录

构建：
```powershell
dotnet build src\EflayGameSaveManager.Avalonia\EflayGameSaveManager.Avalonia.csproj -c Release
```

### `EflayGameSaveManager.Maui`

.NET MAUI Android 客户端，共享 Core 业务逻辑。

当前功能：
- 游戏列表 + 收藏栏，云状态刷新，上传/恢复当前存档
- SAF 文件/文件夹选择器，`content://` → 真实路径解析
- 首次启动种子配置，启动时标准化（合并重复单元）
- `MANAGE_EXTERNAL_STORAGE` + cleartext HTTP
- **Shizuku 支持**：JNI 桥接 Shizuku API，受限路径自动暂存/恢复，全部在 `#if ANDROID` ViewModel 层处理

构建：
```powershell
dotnet build src\EflayGameSaveManager.Maui\EflayGameSaveManager.Maui.csproj -c Debug -f net10.0-android
```

### `EflayGameSaveManager.Kotlin`

原生 Kotlin / Jetpack Compose Android 客户端，独立实现。

当前功能：
- 完整复刻存档管理流程：S3 云同步、zip 归档、收藏、配置标准化
- `kotlinx.serialization` + `@SerialName` 匹配 C# snake_case JSON
- S3 AWS4-HMAC-SHA256 签名（OkHttp），zip 格式兼容 C# `ArchiveTransferService`
- SAF → 真实路径解析，`MANAGE_EXTERNAL_STORAGE`
- **Shizuku 支持**：反射桥接 `newProcess`，`ArchiveService` 内建受限路径接管。Header 状态指示器

构建：
```bash
cd src/EflayGameSaveManager.Kotlin && ./gradlew assembleDebug
```

APK ~17MB，Android Studio 直接打开 `src/EflayGameSaveManager.Kotlin` 即可开发。

### `EflayGameSaveManager.MewUI`

基于 MewUI 的轻量 .NET 桌面版。

当前功能：
- 复用 `EflayGameSaveManager.Core` 的配置读取、路径解析和 S3 兼容云同步
- 查看游戏列表、当前设备存档路径、当前设备游戏路径和基础云状态
- 支持打开存档目录、打开游戏目录、运行游戏、上传当前存档到云端、恢复当前云端存档
- 支持一个基础的”快速添加游戏”流程，可新增一个带单个当前设备文件夹存档路径的游戏，游戏路径可选
- 无需预先存在 `GameSaveManager.config.json`；首次添加游戏后会自动写出配置文件

构建：
```powershell
dotnet build src\EflayGameSaveManager.MewUI\EflayGameSaveManager.MewUI.csproj
```

### `EflayGameSaveManager.Lazarus`

轻量 Lazarus/FreePascal 备用界面。

当前状态：
- 用于 Avalonia 无法运行或启动过慢时尝试
- 支持本地路径编辑、备份、运行游戏、打开目录
- 支持文件夹、文件、注册表存档单元
- 云状态/上传/恢复已在 Lazarus 内置实现（不再依赖 CloudTool）
- 支持重建云备份索引
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

存档单元支持 `Folder`、`File`、`Zip`，以及用 `WinRegistry` 表示的 Windows 注册表键。

云端后端配置位于 `GameSaveManager.config.json` 的 `settings.cloud_settings`。Avalonia 版本可以在 `Settings` 页面配置 S3 endpoint、bucket、region、root path、access key、secret key 和同步选项。

## Zip 模式

当游戏、模拟器、Android 客户端或外部工具已经把完整存档保存成单个 `.zip` 文件时，使用 `Zip` 存档单元。

- 新增或编辑存档单元时，将 `unit_type` 设置为 `Zip`。
- 当前设备路径填写对应的 `.zip` 文件路径。
- 本地备份和云端上传会直接复制这个 zip 文件，不再重新打包内部内容。
- 恢复时会把下载或选中的备份 zip 写回到配置的 zip 路径。
- 建议 Zip 模式的游戏只使用 Zip 存档单元；只有兼容旧配置时再混用 `Folder`、`File` 或 `WinRegistry`。

面向 AI 编程助手和后续维护者的实现说明见 [`docs/AI_DEVELOPMENT_GUIDE.md`](docs/AI_DEVELOPMENT_GUIDE.md)。

## 推荐用法

- 优先使用 Avalonia 版本，它是当前主推版本，可独立创建和维护配置。
- 只有当 Avalonia 无法运行或在目标环境启动过慢时，再尝试 Lazarus 版本。
- Lazarus 云恢复需要可用的 7-Zip 可执行文件在 `bin` 目录或 `settings.seven_zip_dir` 指定目录。

运行：
```text
src\EflayGameSaveManager.Avalonia\bin\Release\net10.0\GameSaveManager.exe
```
