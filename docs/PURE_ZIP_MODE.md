# Pure Zip Mode — 设计文档

## 需求

Android 端支持"纯 Zip 模式"：不配置固定存档路径，每次上传时临时选择一个 zip 文件直传云端。PC 端保持文件夹模式不变。两端共享同一云端游戏条目，互通。

### 典型场景

```
安卓模拟器导出存档zip → 选文件 → 直传云端 ← 云端同一zip → PC下载 → 解压到模拟器目录
```

1. 安卓上用 Sudachi/Citron 等 Switch 模拟器导出存档 → 得到 zip（如 `/sdcard/Download/save.zip`）
2. 在 App 中选择游戏 → 点"上传" → 弹出文件选择器 → 选 zip → 直接上传到云端
3. PC 端点击"恢复" → 下载同一个 zip → 解压到模拟器存档目录
4. PC 端也可以正常上传（打包目录为 zip → 云端），安卓端下载恢复

### 与现有模式的对比

| | Folder/File 模式 | Zip 模式（新增） |
|---|---|---|
| 路径配置 | 固定路径，存在 config 中 | **不存路径**，每次上传时临时选文件 |
| 上传流程 | 读路径 → 打包 zip → 上传 | 选 zip 文件 → 直传 |
| 恢复流程 | 下载 zip → 解压到配置路径 | 下载 zip → 保存到用户选择的位置 |
| 代表设备 | PC、Winlator | Android（模拟器导出场景） |

## 云端兼容性

云端格式不变：

```
/game-save-manager/save_data/{gameName}/
  ├── 2026-05-30_12-00-00.zip    ← PC 打包上传的
  ├── 2026-05-30_13-00-00.zip    ← Android 直传的 zip
  └── Backups.json               ← 共用的清单
```

两个来源的 zip 在云端完全平等，按时间戳区分。PC 下载 Android 上传的 zip 后解压，Android 下载 PC 打包的 zip 后直接保存。

## 数据模型

### SaveUnitType 新增 Zip

```diff
// C# SaveUnitType.cs
public enum SaveUnitType
{
    Folder,
    File,
    WinRegistry,
+   Zip
}

// Kotlin ManagerConfig.kt
@Serializable
enum class SaveUnitType {
    Folder,
    File,
    WinRegistry,
+   Zip
}
```

### SaveUnitDefinition 新增 linked_unit_ids

```diff
// C# SaveUnitDefinition
public sealed class SaveUnitDefinition
{
    public int Id { get; set; }
    public SaveUnitType UnitType { get; set; }
    public Dictionary<string, string> Paths { get; set; } = [];
    public bool DeleteBeforeApply { get; set; }
+   public List<int> LinkedUnitIds { get; set; } = [];
}

// Kotlin SaveUnitDefinition
@Serializable
data class SaveUnitDefinition(
    val id: Int = 0,
    val unit_type: SaveUnitType = SaveUnitType.Folder,
    val paths: Map<String, String> = emptyMap(),
    val delete_before_apply: Boolean = false,
+   val linked_unit_ids: List<Int> = emptyList()
)
```

**关联规则（当前简化版）**：一个 Folder unit 关联一个 Zip unit，双向。`linked_unit_ids` 是集合预留后续扩展，当前只放一个值。
- `id:0` Folder 的 `linked_unit_ids: [1]` → 关联到 `id:1` Zip
- `id:1` Zip 的 `linked_unit_ids: [0]` → 关联到 `id:0` Folder
- 没有关联目标的就为空 `[]`

### Zip unit 的设备归属

Zip 类型不是"没有设备"，而是"属于某个设备，但没有固定路径"。在 `paths` 中写入设备 ID → `""`（空字符串），表示：

- 这个 unit 归属于该设备
- 路径不持久化，每次上传/恢复时由用户选择
- 配置中的空路径使 `hasCurrentDevicePaths` 能正确判断"该设备有存档单元"

```json
{
  "id": 1,
  "unit_type": "Zip",
  "paths": {
    "cd0d180b-fd0e-416b-bb12-11c9b18fdd50": ""
  },
  "linked_unit_ids": [0]
}
```

### 完整配置示例：PC Folder + Android Zip

```json
{
  "name": "塞尔达无双 海拉鲁全明星",
  "save_paths": [
    {
      "id": 0,
      "unit_type": "Folder",
      "paths": {
        "5c13b715-9b50-4ddf-be58-4bb7dbdc3d68": "<winAppData>\\yuzu\\nand\\user\\save\\...\\01002AB007FD2000"
      },
      "delete_before_apply": false
    },
    {
      "id": 1,
      "unit_type": "Zip",
      "paths": {
        "cd0d180b-fd0e-416b-bb12-11c9b18fdd50": ""
      },
      "linked_unit_ids": [0]
    }
  ],
  "game_paths": {},
  "next_save_unit_id": 2,
  "cloud_sync_enabled": true
}
```

**设备说明**：`5c13b715-...` = EFLAYPC（PC 端），`cd0d180b-...` = Android（移动端）。`devices` 字典中已包含两者。

**关键点**：Zip 类型的 `paths` 为空（或只有 device_id → ""）。路径不持久化，每次上传时临时选择。

## 互通验证：无需额外处理

实际 zip 结构简单，内部就是存档文件夹本身：

```
塞尔达无双海拉鲁全明星存档20260107.zip
  └── 01002AB007FD2000/
        ├── save_data_1
        └── save_data_2
```

PC 端配置的存档路径为 `C:\Users\%USERNAME%\...\nand\user\save\...\01002AB007FD2000`。

PC 恢复时的 `ArchiveTransferService.ResolveFolderContentRoot` 逻辑天然兼容：
1. zip 解压后根目录只有一个子目录 `01002AB007FD2000`
2. 目标路径的末尾目录名也是 `01002AB007FD2000`
3. 自动匹配 → 提取 `01002AB007FD2000/` 内容 → 覆盖到目标目录

**两端互通零改动，现有逻辑直接兼容。**

## 恢复时的跨 unit 查找逻辑

Backups.json 的 `device_head` 不变（保持现有格式）。恢复时如果当前设备没有 head，通过 `linked_unit_ids` 查找关联 unit 的 head：

```
恢复 unit 1 (Zip, Android, linked_unit_ids=[0]):
  → 查 device_head["Android-id"] → 有 → 直接用
  → 没有 → 遍历 linked_unit_ids [0]
    → 查 unit 0 有没有 PC 上传过 → device_head["PC-id"] 存在
    → 下载 PC 的 zip → 因为当前 unit 1 是 Zip 类型 → 不解压，直接保存到用户选的位置

恢复 unit 0 (Folder, PC, linked_unit_ids=[1]):
  → 查 device_head["PC-id"] → 有 → 直接用
  → 没有 → 查 unit 1 的 device_head["Android-id"]
    → 下载 → Folder 类型 → 解压到配置目录
```

**Backups.json 不需要改** — 只是恢复时多一个 fallback 查找路径。

## 实现计划

### 第一步：模型

- C# `SaveUnitDefinition` 加 `List<int> LinkedUnitIds`
- C# `SaveUnitType` 加 `Zip`
- Kotlin 对应加 `linked_unit_ids` 和 `Zip`

### 第二步：Kotlin 上传

**ArchiveService**：Zip 类型 → 直接返回选中的 zip，不重新打包
**S3CloudService**：同上

### 第三步：Kotlin 恢复 + 跨 unit 查找

**CloudSyncService / ViewModel**：
- 恢复时先查本设备 head
- 没有则遍历 `linked_unit_ids`，找关联 unit 的 head
- Zip 类型 → 下载不解压；Folder 类型 → 解压到目录

### 第四步：Kotlin UI — 自动关联

- Zip 类型游戏不显示"Edit save path"，显示"Select zip for upload"
- 新建 Zip unit 时自动找已有 Folder unit 建立双向 `linked_unit_ids`
- 点"Upload" → 弹出文件选择器（`*.zip`）→ 直传，不存路径
- 路径不持久化到 config

### 第五步：C# Core + MAUI

同样逻辑。

### 改动量：~80 行代码

## 后续迭代

- [ ] 手动管理关联（UI 上显示/编辑 linked_unit_ids）
- [ ] 一对多关联（一个 Zip 对应多个 Folder）
- [ ] Shizuku 直读模拟器私有目录自动打包
