# Registry JSON Format — 设计文档

## 需求

注册表备份从 `.reg` 文本格式改为 JSON，实现跨平台兼容。JSON 格式的注册表数据可被任意平台读取和写入，兼容 Steam OS 等兼容 Windows 游戏的特殊系统（Proton/Wine），存入云端 zip 后所有端共享。

### 典型场景

1. **Windows 端导出注册表** — 用 .NET `Microsoft.Win32.Registry` API 读取注册表键，递归遍历子键和值，序列化为 JSON 存入 zip
2. **Windows 端导入注册表** — 从 zip 解压 `registry.json`，解析 JSON，用 .NET Registry API 写回注册表
3. **非 Windows 端** — 可解析 JSON 查看注册表内容；Steam OS/Proton 下可将 JSON 转换回 `.reg` 文件后通过 `wine regedit` 导入
4. **云端互通** — 同 `.reg` 一样存储在 zip 内，云端格式不变

## JSON Schema

参考示例文件 `E:\Program Files\RGSM\save_data\测试文件和注册表\registry.json`。

### 顶层结构

```json
{
  "format_version": 1,
  "root_key": "HKEY_CURRENT_USER\\Software\\Example",
  "entries": [
    {
      "subkey": "",
      "values": [...]
    },
    {
      "subkey": "ChildKey",
      "values": [...]
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `format_version` | int | 格式版本，当前为 1 |
| `root_key` | string | 导出的注册表根键路径 |
| `entries` | array | 子键条目列表，第一个条目的 `subkey` 为空串表示 root_key 本身的值 |

### Entry 结构

每个 entry 表示一个注册表子键：

```json
{
  "subkey": "PotPlayer64",
  "values": [
    { "type": "sz",    "name": "LastExePath", "data": "D:\\Program Files\\..." },
    { "type": "dword", "name": "Check146_147", "data": 5 },
    { "type": "dword", "name": "",             "data": 1 }
  ]
}
```

- `subkey` — 相对于 `root_key` 的子键路径。空字符串表示 root_key 本身
- `values` — 该键下的值数组。若子键无值（仅作为容器），可为空数组 `[]`。空数组表示该子键存在，但不含任何值（区别于 null）

### Value 结构

```json
{ "type": "sz", "name": "ValueName", "data": "value" }
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `type` | string | 注册表值类型标识（见下表） |
| `name` | string | 值名称。空字符串表示默认值 `(Default)` |
| `data` | string / int / string[] | 值数据，类型由 `type` 决定 |

### 支持的值类型

| type | 注册表类型 | JSON data 类型 | 示例 |
|------|-----------|---------------|------|
| `sz` | REG_SZ | string | `"Hello"` |
| `dword` | REG_DWORD | int | `5` |
| `qword` | REG_QWORD | number (64-bit int) | `12345678901234` |
| `expand_sz` | REG_EXPAND_SZ | string | `"%AppData%\\Game"` |
| `multi_sz` | REG_MULTI_SZ | string[] | `["line1", "line2"]` |
| `binary` | REG_BINARY | string (base64) | `"AQIDBA=="` |

`data` 为空字符串的 `sz` 表示 REG_SZ 空值，区别于 `""` 字符串。

### 完整示例（PotPlayer）

```json
{
  "format_version": 1,
  "root_key": "HKEY_CURRENT_USER\\Software\\DAUM",
  "entries": [
    {
      "subkey": "",
      "values": [
        { "type": "sz", "name": "GUID_POTPLAYER", "data": "A5B0759DCC8D48099320A089EA7DE371" },
        { "type": "sz", "name": "Install_Date", "data": "2025-05-03" }
      ]
    },
    {
      "subkey": "PotPlayer64",
      "values": [
        { "type": "sz", "name": "LastExePath", "data": "D:\\Program Files\\DAUM\\PotPlayer\\PotPlayerMini64.exe" },
        { "type": "dword", "name": "AddMyComPL", "data": 1 }
      ]
    },
    {
      "subkey": "PotPlayerMini64\\Settings",
      "values": [
        { "type": "dword", "name": "AudioVolume", "data": 48 },
        { "type": "sz", "name": "LastSkinXmlName", "data": "VideoSkin.xml" }
      ]
    }
  ]
}
```

## 与 .reg 格式对比

| | .reg | registry.json |
|---|---|---|
| 可读性 | Windows 特定，ANSI/UTF-16 | UTF-8 JSON，通用可读 |
| 跨平台解析 | 需自写 parser | 任何 JSON 库 |
| 导入工具 | `reg.exe`（仅 Windows） | .NET Registry API / `wine regedit` / 手动解析 |
| 二进制值 | hex 编码 | base64 |
| 文件大小 | 较小 | 略大（~1.5×）但仍在 KB 级 |
| 云端兼容 | 已有 | 同路径，文件名不同 |

## 数据流变化

### 备份流程（新旧对比）

```
Before:
  WinRegistryTransferService.ExportKey()
    → reg.exe export "HKLM\..." "content/1/registry.reg" /y
    → registry.reg (Windows .reg text)

After:
  RegistryJsonService.ExportKey("HKLM\...", "content/1/registry.json")
    → .NET Microsoft.Win32.Registry API 递归遍历
    → registry.json (JSON)
```

### 恢复流程（新旧对比）

```
Before:
  ArchiveTransferService 找到 content/{id}/*.reg
    → WinRegistryTransferService.ImportFile("registry.reg")
    → reg.exe import registry.reg

After:
  ArchiveTransferService 找到 content/{id}/registry.json
    → RegistryJsonService.ImportFile("registry.json")
    → 解析 JSON 后调用 .NET Microsoft.Win32.Registry API 写回
```

### 文件命名

- 旧: `content/{unitId}/registry.reg`
- 新: `content/{unitId}/registry.json`

## 向后兼容

### 旧备份中的 .reg 文件

恢复时需要兼容旧格式。在 `ArchiveTransferService.RestoreCurrentDeviceArchive` 中：

```
查找恢复源文件的优先级：
1. content/{unitId}/registry.json  ← 新格式
2. content/{unitId}/*.reg           ← 旧格式（向后兼容）
```

若找到 `registry.json`，用 `RegistryJsonService.ImportFile`；若找到 `.reg` 文件，用现有的 `WinRegistryTransferService.ImportFile`。

### 导出策略

新备份**全部使用** `registry.json`，不再产生 `.reg` 文件。不需要向后兼容导出，因为旧版本遇到新格式 zip 只是无法导入注册表部分，不影响 Folder/File 类型存档。

### SaveUnitType 不变

`WinRegistry` 枚举值保持不变。格式变化是 `WinRegistryTransferService` 内部的实现细节，不影响配置模型。

### Backups.json 不变

`Backups.json` 清单不感知 zip 内部格式，无需改动。

## 实现计划

### 第一步：C# Core — 数据模型

新增 `src/EflayGameSaveManager.Core/Models/RegistryJsonModels.cs`：

```csharp
public sealed record RegistryJsonRoot(
    int FormatVersion,
    string RootKey,
    IReadOnlyList<RegistryEntry> Entries);

public sealed record RegistryEntry(
    string Subkey,
    IReadOnlyList<RegistryValue> Values);

public sealed record RegistryValue(
    string Type,
    string Name,
    object? Data);  // string | int | long | string[] | null
```

### 第二步：C# Core — RegistryJsonService

新增 `src/EflayGameSaveManager.Core/Services/RegistryJsonService.cs`：

- **`ExportKey(string registryPath, string outputJsonFile) -> bool`**
  - 用 `Microsoft.Win32.RegistryKey.OpenBaseKey()` 解析 root hive
  - 递归遍历子键和值
  - 映射值类型：`RegistryValueKind` → JSON type string
  - REG_BINARY → base64
  - REG_MULTI_SZ → string[]
  - 序列化为 JSON 写入文件
  - 非 Windows 返回 false

- **`ImportFile(string jsonFile) -> bool`**
  - 读取 JSON，反序列化为 `RegistryJsonRoot`
  - 用 .NET Registry API 逐键写入
  - 写入前可选 `DeleteKey` 清理（由调用方控制）
  - 非 Windows 返回 false

- **`KeyExists(string registryPath) -> bool`** — 保留原有逻辑（或从 WinRegistryTransferService 移入）
- **`DeleteKey(string registryPath) -> bool`** — 同

### 第三步：C# Core — 整合到 ArchiveTransferService

修改 `ArchiveTransferService.cs`：

1. 导出文件名: `registry.reg` → `registry.json`
2. 调用: `_winRegistryTransferService.ExportKey()` → `_registryJsonService.ExportKey()`
3. 恢复时优先找 `registry.json`，fallback `*.reg`
4. 导入: `_winRegistryTransferService.ImportFile()` → `_registryJsonService.ImportFile()`

### 第四步：C# Core — WinRegistryTransferService 处理

`WinRegistryTransferService` 保留但职责缩小：

- `KeyExists` — 保留（`reg.exe query` 仍可用）
- `DeleteKey` — 保留（`reg.exe delete` 仍可用）
- `ExportKey` — **弃用**，由 `RegistryJsonService.ExportKey` 替代
- `ImportFile` — **保留**用于向后兼容旧 `.reg` 文件

或者将 `KeyExists` / `DeleteKey` 也迁移到 `RegistryJsonService` 中统一管理，`WinRegistryTransferService` 变为仅用于旧 `.reg` 导入的 fallback 类。

### 第五步：跨平台处理

- **Windows 导出/导入** — 用 .NET `Microsoft.Win32.Registry` API，不再依赖 `reg.exe`
- **非 Windows 导出** — 返回 false / 跳过（无注册表可读）
- **非 Windows 导入** — 当前跳过。未来可能的扩展：
  - Steam OS (Proton): 将 JSON 转回 `.reg` 文本，通过 `wine regedit /S file.reg` 导入 Wine prefix
  - 此项作为后续迭代，本次不实现

### 第六步：文档更新

- 更新 `docs/AI_DEVELOPMENT_GUIDE.md` 第 9.3 节注册表处理
- 更新 `README.md` 存档单元类型说明
- 在 `GameSaveManager.config.json` 的测试条目附注 JSON 格式

## 影响范围

| 组件 | 变更 |
|------|------|
| Core — RegistryJsonModels.cs | **新增** |
| Core — RegistryJsonService.cs | **新增** |
| Core — WinRegistryTransferService.cs | 缩小职责：保留旧 .reg 导入 + KeyExists/DeleteKey |
| Core — ArchiveTransferService.cs | 导出用 JSON，恢复兼容新旧格式 |
| Core — SaveBackupService.cs | 同样 `registry.reg` → `registry.json` |
| Avalonia | 无 UI 变更（枚举不变） |
| Lazarus | 需要 Pascal 端适配（导出到 `registry.json`，恢复时兼容 `.reg` 和 `.json`） |
| MAUI | 无需变更（Android 端 WinRegistry 本身是 no-op） |
| Kotlin | 新增 JSON 解析模型（可选，目前 WinRegistry 是 no-op） |

## 风险与注意事项

1. **REG_BINARY 编码**: `.reg` 用 hex，JSON 用 base64。需确保转换正确无损
2. **REG_MULTI_SZ**: `.reg` 用 `\0` 分隔，JSON 用 string[]。需确保转义正确
3. **注册表权限**: `Microsoft.Win32.Registry` API 受 UAC 限制，某些 `HKEY_LOCAL_MACHINE` 键可能需管理员权限
4. **64/32 位视图**: `RegistryKey.OpenBaseKey()` 可指定 `RegistryView.Registry64` / `RegistryView.Registry32`，需与 `root_key` 路径匹配
5. **大注册表键**: 遍历深层嵌套子键可能较慢，可加递归深度限制（如 32 层）
6. **format_version**: 预留给未来 schema 变化，version 1 为当前定义

## 后续迭代

- [ ] Steam OS / Proton 注册表导入: `registry.json` → `.reg` → `wine regedit`
- [ ] Lazarus Pascal 端 JSON 导出/导入实现
- [ ] Kotlin 端 registry.json 模型定义（用于查看注册表内容，非导入）
- [ ] 单元测试：roundtrip 测试（导出 → 删除 → 导入 → 验证值一致）
