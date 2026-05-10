# Eflay Game Save Manager 开发说明

本文档面向后续维护者和 AI 编程助手。目标是让读者即使暂时没有项目代码，也能理解本软件的作用、数据格式、核心流程，并能复刻出功能相近的游戏存档管理工具。

## 1. 软件定位

Eflay Game Save Manager 是一个游戏存档管理器，用一份共享 JSON 配置维护：

- 游戏名称
- 每台设备上的游戏可执行文件路径
- 每台设备上的存档位置
- 存档类型：文件夹、单文件、Windows 注册表键
- 云同步配置
- 当前设备识别与路径覆盖

核心目标是让同一个游戏在多台 Windows/Winlator 设备之间备份、上传、下载和恢复存档。软件把每个游戏的当前设备存档打包成 zip，再上传到 S3 兼容对象存储。恢复时下载 zip，并按配置中的存档单元还原到当前设备路径。

## 2. 项目结构

主要项目：

- `src/EflayGameSaveManager.Core`
  - 共享业务逻辑库。
  - 负责配置读写、设备识别、路径 token 解析、本地备份、zip 打包/还原、注册表导入导出、S3 云同步。
- `src/EflayGameSaveManager.Avalonia`
  - 常规 Windows 桌面 UI。
  - 支持查看游戏、编辑路径、添加游戏、选择文件/文件夹、云同步和恢复。
- `src/EflayGameSaveManager.Lazarus`
  - FreePascal/Lazarus 轻量 UI，主要面向 Winlator。
  - 内置 S3 状态/上传/恢复逻辑，恢复 zip 解压依赖外部 7-Zip。
- `src/EflayGameSaveManager.CloudTool`
  - 命令行工具，用 Core 实现脚本化云操作。
- `src/EflayGameSaveManager.Lvgl`
  - 实验性 LVGLSharp UI，不是当前主线。
- `tests/EflayGameSaveManager.Core.Tests`
  - Core 层测试。

关键文件：

- `GameSaveManager.config.json`
  - 主配置文件。
- `GameSaveManager.runtime.json`
  - 当前设备运行时覆盖，例如 `forced_device_name`。
- `GameSaveManagerLite.config.json`
  - Lazarus Lite 专用覆盖，例如 `forced_device_name` 和 `seven_zip_dir`。

## 3. 配置文件格式

主配置文件名固定为 `GameSaveManager.config.json`。程序会从运行目录开始向父目录查找。Avalonia 版本如果找不到配置，会创建内存空配置；用户添加第一个游戏后，会在运行目录写出新的 `GameSaveManager.config.json`。

最小可用结构：

```json
{
  "version": "1.0.0",
  "backup_path": "./save_data",
  "games": [],
  "settings": {
    "locale": "zh_SIMPLIFIED",
    "log_to_file": true,
    "cloud_settings": {
      "always_sync": false,
      "auto_sync_interval": 0,
      "root_path": "/game-save-manager",
      "backend": {
        "type": "S3",
        "endpoint": "",
        "bucket": "",
        "region": "",
        "access_key_id": "",
        "secret_access_key": ""
      },
      "max_concurrency": 1
    }
  },
  "favorites": [],
  "quick_action": {
    "quick_action_game": null,
    "hotkeys": {
      "apply": ["", "", ""],
      "backup": ["", "", ""]
    }
  },
  "devices": {}
}
```

游戏条目格式：

```json
{
  "name": "Example Game",
  "save_paths": [
    {
      "id": 0,
      "unit_type": "Folder",
      "paths": {
        "device-id-a": "<home>\\Saved Games\\Example"
      },
      "delete_before_apply": false
    },
    {
      "id": 1,
      "unit_type": "File",
      "paths": {
        "device-id-a": "<winLocalAppData>\\Example\\save.dat"
      },
      "delete_before_apply": false
    },
    {
      "id": 2,
      "unit_type": "WinRegistry",
      "paths": {
        "device-id-a": "HKEY_CURRENT_USER\\Software\\Example"
      },
      "delete_before_apply": false
    }
  ],
  "game_paths": {
    "device-id-a": "D:\\Games\\Example\\Example.exe"
  },
  "next_save_unit_id": 3,
  "cloud_sync_enabled": true
}
```

字段说明：

- `name`
  - 游戏显示名称，也是云端路径的一部分。进入云端路径前需要做文件名/路径段清洗。
- `save_paths`
  - 一个游戏可以有多个存档单元。
- `save_paths[].id`
  - 存档单元 ID。打包进 zip 时用作顶层目录名，例如 `0/`、`1/`。
- `save_paths[].unit_type`
  - `Folder`：整个目录。
  - `File`：单个文件。
  - `WinRegistry`：Windows 注册表键。
- `save_paths[].paths`
  - 按设备 ID 映射路径。当前设备没有路径时，可以回退到任一已有路径作为编辑默认值，但保存时应写入当前设备 ID。
- `save_paths[].delete_before_apply`
  - 恢复前是否删除目标。文件型删除文件，文件夹型删除目录，注册表型删除注册表键。
- `game_paths`
  - 按设备 ID 映射游戏可执行文件路径，可为空。
- `next_save_unit_id`
  - 下一个存档单元 ID。添加游戏时可设置为当前 `save_paths` 数量。
- `cloud_sync_enabled`
  - 是否允许该游戏云同步。

设备格式：

```json
{
  "devices": {
    "uuid": {
      "id": "uuid",
      "name": "DESKTOP-NAME"
    }
  }
}
```

设备识别逻辑：

1. 使用 `GameSaveManager.runtime.json` 中的 `forced_device_name`，如果存在。
2. 否则使用 `Environment.MachineName`。
3. 在 `devices` 里按名称查找。
4. 找不到则生成新的 GUID 写入 `devices`。

## 4. 路径 Token

用户可以在配置路径里使用 token，运行时解析为实际目录。

支持的 token：

- `<home>`：用户目录，例如 `C:\Users\name`
- `<winDocuments>`：文档目录
- `<winAppData>`：`AppData\Roaming`
- `<winLocalAppData>`：`AppData\Local`
- `<winLocalAppDataLow>`：`AppData\LocalLow`
- `<winCommonAppData>`：`ProgramData`
- `<winCommonDocuments>`：公共文档目录
- `<winPublic>`：当前实现等同公共文档目录
- `<winDesktop>`：桌面目录

实现要求：

- token 替换应大小写不敏感。
- token 替换后再调用环境变量展开，例如 `%USERPROFILE%`。
- UI 中路径既允许选择，也允许手动输入 token。

对应文件：

- `src/EflayGameSaveManager.Core/Services/EnvironmentTokenResolver.cs`

## 5. Core 领域模型

模型文件：

- `Models/ManagerConfig.cs`
  - JSON 配置根对象、游戏、存档单元、云设置、设备设置。
- `Models/SaveUnitType.cs`
  - `Folder`、`File`、`WinRegistry`。
- `Models/AppSnapshot.cs`
  - UI 使用的只读快照：配置路径、备份根目录、游戏列表、当前设备。
- `Models/CurrentDeviceModels.cs`
  - 当前设备上下文、当前设备路径信息。
- `Models/CloudTransferModels.cs`
  - 云状态、备份记录、上传/下载结果。
- `Models/AppRuntimeSettings.cs`
  - 运行时配置。

推荐架构：

- 配置模型保持接近 JSON 格式。
- UI 不直接操作复杂 JSON，应通过 ViewModel 修改 `ManagerConfig`，再保存。
- 运行时展示使用 `GameLibraryService.CreateSnapshot` 生成快照，避免 UI 层重复解析设备、token 和备份路径。

## 6. 配置读写流程

对应文件：

- `Services/GameSaveManagerConfigurationService.cs`
- `Serialization/ManagerJsonSerializerContext.cs`

职责：

- 从运行目录向父目录查找 `GameSaveManager.config.json`。
- 找不到时，Avalonia UI 使用 `CreateDefault()` 创建内存默认配置。
- 保存时写到临时文件 `GameSaveManager.config.json.tmp`，再覆盖目标文件。
- 保存前创建目标目录。
- 使用 `System.Text.Json` 源生成上下文，兼容 AOT/Trim。

复刻实现要点：

1. 定义配置文件名常量。
2. `FindConfigPath(startDirectory)` 从目录向上查找。
3. `GetDefaultConfigPath(startDirectory)` 返回默认写出位置。
4. `LoadAsync(path)` 反序列化 JSON。
5. `SaveAsync(path, config)` 先写 `.tmp`，再原子替换。
6. `CreateDefault()` 返回空游戏、空设备、可用默认设置。

## 7. 游戏快照与当前设备

对应文件：

- `Services/CurrentDeviceService.cs`
- `Services/GameLibraryService.cs`
- `Services/AppRuntimeSettingsService.cs`

核心流程：

1. 加载配置。
2. 加载运行时设置，读取 `forced_device_name`。
3. 确定当前设备 ID 和名称，必要时添加新设备。
4. 解析备份根目录：
   - `backup_path` 支持 token。
   - 相对路径相对于配置文件所在目录。
5. 对每个游戏生成快照：
   - 保存所有设备路径。
   - 当前设备路径从 `save_paths[].paths[currentDeviceId]` 获取。
   - 当前设备没有路径时，回退到该存档单元已有的第一个路径。
   - 所有路径经过 token 解析。

## 8. 本地备份

对应文件：

- `Services/SaveBackupService.cs`

备份目录结构：

```text
save_data/
  GameName/
    yyyyMMdd-HHmmss/
      DeviceName/
        unit-0/
          ...
        unit-1/
          ...
```

处理逻辑：

- `Folder`：递归复制整个目录。
- `File`：复制单文件，保留原文件名。
- `WinRegistry`：调用注册表导出逻辑，将键导出为 `registry.reg`。

如果路径不存在，应跳过，不应中断整个备份。

## 9. 云同步归档

对应文件：

- `Services/ArchiveTransferService.cs`
- `Services/CloudSyncService.cs`
- `Services/CloudStoragePathHelper.cs`
- `Services/S3CompatibleCloudStorageClient.cs`
- `Services/WinRegistryTransferService.cs`

### 9.1 上传当前设备存档

创建临时工作目录：

```text
%TEMP%/EflayGameSaveManager/upload/{guid}/
  content/
    0/
    1/
    2/
  current-device-save.zip
```

每个存档单元进入 `content/{unitId}`：

- `Folder`：复制目录内容到 `content/{unitId}`。
- `File`：复制文件到 `content/{unitId}/{fileName}`。
- `WinRegistry`：导出到 `content/{unitId}/registry.reg`。

然后压缩 `content` 目录，zip 内顶层就是 `0/`、`1/` 等 unit 目录。

### 9.2 恢复当前设备存档

恢复流程：

1. 下载 zip 到临时目录。
2. 解压到临时 `extractRoot`。
3. 对每个本地存档单元找到源目录：
   - 优先 `extractRoot/{unitId}`。
   - 单存档单元时兼容旧 zip，可直接使用 `extractRoot`。
   - 多存档单元且只有一个子目录时，也兼容该子目录。
4. 如果 `delete_before_apply` 为 true：
   - `File`：删除目标文件。
   - `Folder`：删除目标目录。
   - `WinRegistry`：删除目标注册表键。
5. 还原：
   - `File`：取源目录下第一个文件复制到目标文件路径。
   - `Folder`：递归复制源目录到目标目录。若 zip 内多包了一层同名目录，则自动拆掉这一层。
   - `WinRegistry`：导入源目录下第一个 `.reg` 文件。
6. 清理临时目录。

### 9.3 注册表处理

注册表通过 Windows 自带 `reg.exe` 完成：

- 查询：`reg.exe query <key>`
- 导出：`reg.exe export <key> <file> /y`
- 导入：`reg.exe import <file>`
- 删除：`reg.exe delete <key> /f`

使用 `reg.exe` 的原因：

- `.reg` 是标准、可检查、可人工导入的格式。
- FreePascal/Lazarus 版本也能复用相同方式。
- 避免手工遍历注册表时遗漏值类型、二进制、多字符串、子键和 32/64 位视图细节。

非 Windows 环境下，注册表操作应安全返回失败或跳过。

### 9.4 云端对象布局

云根路径由 `settings.cloud_settings.root_path` 决定，例如 `/game-save-manager`。

一个游戏的云路径：

```text
{root_path}/{safeGameName}/
  Backups.json
  yyyy-MM-dd_HH-mm-ss.zip
```

`Backups.json` 保存备份列表和每台设备的 head：

```json
{
  "backups": [
    {
      "date": "2026-05-10_12-00-00",
      "describe": "",
      "path": "save_data\\GameName\\2026-05-10_12-00-00.zip",
      "size": 12345,
      "parent": null,
      "device_id": "device-id"
    }
  ],
  "device_heads": {
    "device-id": "2026-05-10_12-00-00"
  }
}
```

实现要点：

- 上传 zip 后更新 `Backups.json`。
- `device_heads[currentDeviceId]` 指向当前设备最近一次上传。
- 状态查询读取 `Backups.json`，判断当前设备 head 是否存在。
- 支持重建 manifest：扫描云端 zip 对象，重新生成 `Backups.json`。

## 10. S3 兼容存储

云后端配置：

```json
{
  "type": "S3",
  "endpoint": "http://127.0.0.1:9000",
  "bucket": "game-save",
  "region": "local",
  "access_key_id": "minio",
  "secret_access_key": "password"
}
```

Core 使用 `S3CompatibleCloudStorageClient`。Lazarus 使用 `UPascalS3Client.pas` 和 `USha256.pas` 自己实现 S3 签名请求。

复刻时可选择任一 S3 SDK，也可以自行实现 AWS Signature V4。需要支持：

- 上传文件
- 下载文件
- 上传 UTF-8 JSON
- 下载 UTF-8 JSON
- 列出对象
- 删除对象

## 11. Avalonia UI 功能

对应文件：

- `src/EflayGameSaveManager.Avalonia/Views/MainWindow.axaml`
- `src/EflayGameSaveManager.Avalonia/ViewModels/MainWindowViewModel.cs`
- `src/EflayGameSaveManager.Avalonia/Services/IPathPickerService.cs`
- `src/EflayGameSaveManager.Avalonia/Services/AvaloniaPathPickerService.cs`

主要功能：

- 顶部状态栏：
  - 当前状态
  - 配置路径
  - 备份根目录
  - 当前设备
  - 云目标
- 左侧切换：
  - `Games`：游戏列表
  - `Add Game`：添加游戏入口
- 游戏详情：
  - 游戏名称、存档数量、云状态
  - 上传当前存档到云
  - 从云恢复当前存档
  - 运行游戏
  - 打开游戏目录
  - 编辑当前设备游戏路径
  - 编辑当前设备存档路径
  - 查看云备份列表
  - 下载/恢复/删除云备份
  - 重建云备份索引
- 添加游戏：
  - 必填游戏名称
  - 可选游戏可执行文件路径
  - 添加 `Folder`、`File`、`WinRegistry` 存档单元
  - 文件夹/文件路径可选择也可手动输入
  - 注册表路径手动输入
  - 显示 token 示例和当前机器实际位置
  - 保存后写入配置，刷新快照，并选中新游戏

添加游戏写入逻辑：

1. 校验游戏名非空。
2. 校验没有同名游戏。
3. 至少一个存档路径非空。
4. 为每个存档单元分配从 0 开始的 ID。
5. 将路径写入当前设备 ID。
6. 如果填写了游戏路径，写入 `game_paths[currentDeviceId]`。
7. `cloud_sync_enabled` 默认 true。
8. 保存配置文件。

## 12. Lazarus Lite 功能

对应文件：

- `src/EflayGameSaveManager.Lazarus/UMainForm.pas`
- `src/EflayGameSaveManager.Lazarus/UPascalS3Client.pas`
- `src/EflayGameSaveManager.Lazarus/USha256.pas`

功能：

- 读取主配置。
- 读取 Lite 配置覆盖。
- 列出游戏。
- 编辑当前设备存档路径和游戏路径。
- 本地备份。
- 运行游戏。
- 打开路径。
- 云状态、上传、恢复、重建索引。
- 使用外部 7-Zip 解压云端 zip。
- 支持 `Folder`、`File`、`WinRegistry`。

Winlator 场景推荐使用 Lazarus，因为启动更轻。

## 13. CloudTool 命令行

对应文件：

- `src/EflayGameSaveManager.CloudTool/Program.cs`

用途：

- 自动化脚本。
- 排查云同步问题。
- 为其他轻量 UI 提供 fallback。

典型命令：

```powershell
GameSaveManager.CloudTool.exe --action status --config GameSaveManager.config.json --game "Example Game"
GameSaveManager.CloudTool.exe --action upload-current --config GameSaveManager.config.json --game "Example Game"
GameSaveManager.CloudTool.exe --action restore-current --config GameSaveManager.config.json --game "Example Game"
GameSaveManager.CloudTool.exe --action init-backup-dirs --config GameSaveManager.config.json
```

## 14. 错误处理与日志

对应文件：

- `Services/AppLogger.cs`

原则：

- 单个存档路径不存在时跳过，不让整个游戏失败。
- 云操作异常应展示到 UI 状态栏，并写日志。
- 全局未处理异常写日志。
- 注册表导入导出失败写日志。
- 找不到配置时，Avalonia 允许继续，添加游戏后生成配置。

## 15. 测试重点

对应目录：

- `tests/EflayGameSaveManager.Core.Tests`

当前覆盖方向：

- 配置文件可反序列化。
- 默认配置可保存和重新读取。
- token 解析。
- 当前设备识别与路径回退。
- zip 恢复兼容旧布局。
- 云路径生成。
- 云 manifest 序列化。

复刻项目时建议至少覆盖：

- `Folder`、`File`、`WinRegistry` 三种存档类型的打包和恢复。
- 单 unit / 多 unit zip 布局。
- 目标删除逻辑。
- 找不到配置文件时创建默认配置。
- S3 对象 key 规范化。
- 同名游戏拒绝添加。

## 16. 从零复刻的推荐模块划分

如果不依赖本项目代码，从零实现类似软件，可以按以下模块拆分：

1. `ConfigService`
   - 查找、加载、保存、创建默认配置。
2. `DeviceService`
   - 当前设备识别、设备注册、当前设备路径读写。
3. `TokenResolver`
   - token 和环境变量解析。
4. `GameLibraryService`
   - 将配置转换成 UI 快照。
5. `ArchiveService`
   - 当前设备存档打包成 zip。
   - zip 恢复到当前设备。
6. `RegistryService`
   - 注册表 query/export/import/delete。
7. `LocalBackupService`
   - 本地时间戳备份目录。
8. `CloudStorageClient`
   - S3 兼容对象存储抽象。
9. `CloudSyncService`
   - 上传、状态、列表、下载、恢复、删除、重建 manifest。
10. `DesktopUI`
    - 游戏列表、添加游戏、路径编辑、云操作。
11. `CommandLineTool`
    - 暴露核心云操作给脚本。

## 17. 关键不变量

- `save_paths[].id` 必须稳定，云 zip 中按 ID 匹配。
- 同一游戏可有多个存档单元。
- 同一存档单元可有多台设备路径。
- 当前设备路径应写回当前设备 ID，不应覆盖其他设备路径。
- `backup_path` 相对路径基于配置文件所在目录。
- 路径 token 在执行文件/目录操作前解析。
- 云端对象 key 使用清洗后的游戏名，避免非法路径字符。
- `WinRegistry` 存档在归档中表现为 `.reg` 文件。
- 恢复后必须清理临时目录。
- 配置保存应使用临时文件，避免写一半导致配置损坏。

## 18. 常用构建命令

Avalonia：

```powershell
dotnet build src\EflayGameSaveManager.Avalonia\EflayGameSaveManager.Avalonia.csproj -c Release
```

Core 测试：

```powershell
dotnet test tests\EflayGameSaveManager.Core.Tests\EflayGameSaveManager.Core.Tests.csproj -c Release
```

Lazarus：

```powershell
& 'F:\Program\lazarus\lazbuild.exe' --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

CloudTool：

```powershell
dotnet build src\EflayGameSaveManager.CloudTool\EflayGameSaveManager.CloudTool.csproj -c Release
```
