# 《咩咩启示录》公众版配置说明（可交给 AI 阅读）

本文描述本包实际读取的 JSON 格式。JSON 不支持注释或末尾多余逗号。
Windows 反斜杠必须写成 `\\`，Android 路径使用 `/`。

## 1. 哪个文件生效

| 文件 | 位置及用途 |
| --- | --- |
| `GameSaveManager.config.json` | Windows EXE 同目录；游戏、设备、路径和云设置 |
| `GameSaveManager.runtime.json` | Windows EXE 同目录；固定本机逻辑设备为 PC、界面语言 |
| Android 应用内 Config | 这是 Android 实际配置；首次启动从 APK 的脱敏模板复制 |
| Android 目录附带的两个 JSON | 与 APK 内置初始配置相同，供查阅/复制，不会自动导入已安装应用 |

Android 沿用原助手包名：`com.eflay.gamesavemanager.maui`，仅预置此游戏配置，不另设包名。
首次安装或清除本助手数据后，启动会写入脱敏预设；覆盖安装保留原配置，不会强制覆盖。
已有用户可在 Config 页面粘贴本包 Android 目录的配置后保存。
手机原 runtime 的 `forced_device_name` 必须匹配新配置的 `Android` 设备名；若以前改过，
可使配置中的设备名与其一致，或先备份配置，再清除本助手数据，重新启动初始化两份 JSON。
Android 实际配置位于应用私有 Files 目录；通常不应通过文件管理器直接编辑。
用 Menu → Config → Reload → 修改 → Save。Save 后应用会重新读取配置。
默认收藏为空，可在 Games 页选游戏并添加 Favorite。
Windows 编辑文件前退出程序，修改后重新运行。

## 2. 固定两个逻辑设备

```json
"devices": {
  "pc": {"id": "pc", "name": "PC", "platform": "windows"},
  "android": {"id": "android", "name": "Android", "platform": "android"}
}
```

`devices` 的字典键、内部 `id`、`save_paths[].paths` 的键、`game_paths` 的键要一致。
这些 ID 是公开别名，不需要换成 GUID，也不绑定真实硬件。

Windows runtime 为 `{"forced_device_name":"PC","language":"zh-CN"}`；
Android runtime 为 `{"forced_device_name":"Android","language":"zh-CN"}`。
设备按 `name` 匹配，不要删除对应设备或只改其中一个名字，否则应用可能注册新的设备。
不要把 Windows runtime 复制覆盖 Android runtime。

如果只有一台 PC、一台手机，保留现有设备键和名称即可。
多台 PC/手机要给每台新增不同的设备项，同时给所有需要的存档单元添加相同的设备键，
并设置各自 runtime 的 `forced_device_name`。当前包只预设 PC、Android 两台。

## 3. 游戏与存档路径

`games` 数组只含一项，`name` 固定为 `Cult of the Lamb`，`chinese_name` 为 `咩咩启示录`。
`name` 用于云路径、查找和格式转换识别；两端保持一致，修改它会改变云目录。
`chinese_name` 用于显示，`note` 为说明。
本版修复了安卓配置合并时丢失 `contents_only` / `exclude` 的问题。安卓启动会把本游戏的
Folder 单元修复为 content 模式；两端均以 `0/content/...` 打包，恢复后文件直接落在配置目录根下。
旧安卓备份的 `0/user/...` 也会正确展开到 PC `saves` 根目录。

程序的 ZIP 模式同样支持 `contents_only`：开启时只使用最外层文件夹的内容，关闭时保留该文件夹名。
安卓新增和编辑页面均提供此开关，上传及恢复都会使用它，重复同步不会持续剥离内部目录。
本公开模板仍默认使用 Folder + content 模式；不要仅因程序新增 ZIP 开关就改变本游戏的存档类型。

```json
"save_paths": [
  {
    "id": 0,
    "unit_type": "Folder",
    "paths": {
      "pc": "<winLocalAppDataLow>\\Massive Monster\\Cult Of The Lamb\\saves",
      "android": "/storage/emulated/0/Android/data/com.cotl.port/files/user"
    },
    "delete_before_apply": false,
    "contents_only": true,
    "exclude": ["settings*", "Prefs.dict"],
    "linked_unit_ids": []
  }
]
```

| 字段 | 意义/修改约束 |
| --- | --- |
| `id` | 存档单元编号。本包只有 `0`，PC、Android 共用同一单元，不能各自随意更换 |
| `unit_type` | 本包为 `Folder`，表示目录 |
| `paths.pc` | 当前 Windows 用户的 saves 目录；不是游戏 EXE 目录 |
| `paths.android` | Android 实际 user 存档目录；其他移植版应核对包名和位置 |
| `contents_only` | 保持 `true`，只同步目录内容，避免 `saves/user` 名称不同造成额外套一层目录 |
| `exclude` | 保留本机设置。`settings*` 和 `Prefs.dict` 不参与备份/覆盖，避免两端设置冲突 |
| `delete_before_apply` | 本包设 `false`；恢复时合并，不先清空整个目录。仍会覆盖同名进度文件 |
| `linked_unit_ids` | 本包为空，不需要设置链接关系 |
| `next_save_unit_id` | 下一新增单元编号，本包为 `1` |
| `game_paths` | 可留空。若要启动 PC 游戏或准确检测版本，填 `{"pc":"D:\\Games\\Cult of the Lamb\\Cult Of The Lamb.exe"}`，示例需改成自己的路径 |
| `external_launchers` | 外部启动工具，本包为空 |

`<winLocalAppDataLow>` 自动展开到当前用户 `AppData\LocalLow`，不会使用作者用户名。
PC 示例目录不一定适合所有版本，先确认里面存在真实的 slot 存档文件。
Android 示例只适用于 `com.cotl.port`。不要填写存档助手自己的 Files/Cache 目录。
受限目录读取失败时检查 Shizuku 是否启动、授权及其实际权限；ADB 模式通常无法访问
其他应用的 `/data/data/`。按移植版实际存储位置配置，不要凭猜测切换到私有目录。

## 4. 使用自己的云空间

本包没有公共共享服务器，也没有作者的 MinIO 地址、账号或密钥。
初始 `endpoint`、`bucket`、`access_key_id`、`secret_access_key` 都是空字符串，
`games[0].cloud_sync_enabled` 为 `false`，默认不会连接任何云存储。

两端填写相同的 S3 兼容后端（可以是使用者自己的 MinIO 或其他 S3 兼容服务）：

```json
"cloud_settings": {
  "always_sync": false,
  "auto_sync_interval": 0,
  "root_path": "cotl-public",
  "backend": {
    "type": "S3",
    "endpoint": "https://s3.example.invalid",
    "bucket": "YOUR_BUCKET",
    "region": "us-east-1",
    "access_key_id": "YOUR_ACCESS_KEY",
    "secret_access_key": "YOUR_SECRET_KEY"
  },
  "max_concurrency": 1
}
```

上面是不可直接连接的示例。必须替换为自己的有效值，再将游戏项的
`"cloud_sync_enabled": false` 改为 `true`。桌面设置页可保存 S3 字段；
游戏云同步开关可通过 JSON 修改。两端均须配置。

| 字段 | 含义 |
| --- | --- |
| `type` | 保持 `S3` |
| `endpoint` | S3 API 完整地址（含 `https://`），不是 MinIO 管理控制台地址；不要在 URL 中附带用户名/密码 |
| `bucket` | 使用者已有且有权限访问的 bucket 名，不能把 bucket 路径拼进 endpoint |
| `region` | 服务实际区域；按提供方要求填写 |
| `access_key_id` / `secret_access_key` | 使用者自己的凭据；保存在本机 JSON 中，不要公开或发给 AI |
| `root_path` | 此组设备共用的云前缀。模板为 `cotl-public`，可以换成自选前缀，但两端必须一致 |
| `always_sync` / `auto_sync_interval` | 本包禁用自动同步。初次使用先手动上传和恢复 |
| `max_concurrency` | 并发数，本包取 `1` |

云对象大致位于 `<root_path>/save_data/Cult of the Lamb/`，同时维护备份索引和同步状态。
不同使用者不要共用同一云前缀；同一使用者的 PC、Android 必须共用前缀才能互相找到备份。
自建服务需要能被两端访问；手机填 `localhost` 会指向手机自身。
Windows 本地备份在 EXE 同目录 `save_data` 下，由顶层 `backup_path: "./save_data"` 控制。

## 5. 存档格式与使用顺序

程序按游戏名称和目标设备平台选择《咩咩启示录》格式转换插件。
本包支持旧版 PC AES/JSON 与指定 Android 移植版 ZB/gzip/JSON 的转换。
这不保证游戏版本、DLC、模组和存档内部结构跨版本兼容。
PC 1.5.x+ 的 `*.mp` / MessagePack 转换尚未实现；程序会拒绝把 JSON 写成该格式。
填写真实 `game_paths.pc` 有助于检测游戏版本。不要改后缀绕过格式检查。

每次切换设备：退出游戏 → 在最新进度设备上传 → 确认成功/时间 → 另一端恢复 → 再启动游戏。
Android `Restore latest` 取整个游戏云端最新备份，不能自动判断哪份进度最正确。
首次跨端恢复前分别手动复制原始存档到独立位置；本包不附带其他人的存档。

## 6. 其他顶层字段

`version` 为配置格式标签；保持 `1.0.0`。
`favorites` 初始为空；`quick_action.quick_action_game` 为 `null`，快捷键数组为空值。
`settings.locale` 为 `zh_SIMPLIFIED`，`settings.log_to_file` 控制日志记录。
runtime 的 `language` 使用 `zh-CN`。这些字段不用为云同步修改。
本包保留通用助手界面，初始配置只列出这一个游戏，并未移除手动添加其他游戏的能力。

## 7. 复制给 AI 的提问模板

```text
我在用《咩咩启示录存档助手》公众发布包。请先阅读附带的 CONFIG_GUIDE.md，
然后按本包真实 JSON schema 帮我修改配置，输出可直接使用的完整合法 JSON。

目标：Windows PC 与 Android 移植版共用自己的 S3 兼容云空间。
限制：只保留 Cult of the Lamb / 咩咩启示录；设备只保留 pc、android；
对应名称 PC、Android；不要生成或使用别人的 GUID，不要添加其他游戏/设备；
保留单元 id=0、contents_only=true、exclude=["settings*","Prefs.dict"]；
不要编造 MinIO 地址、用户名、凭据或包名；不确定的路径请询问我。
不要修改 games[0].name，不能通过改名让两个设备使用不同云目录。

我的环境：
- Windows 游戏版本：<填写>，游戏 EXE：<填写自己的路径>
- Windows 存档目录：<填写>
- Android 游戏包名、版本和存档目录：<填写>
- S3 API endpoint、bucket、region、root_path：<填写公开占位符或自己的非敏感描述>
- access_key_id / secret_access_key：请保留 YOUR_ACCESS_KEY / YOUR_SECRET_KEY 占位符，
  我会在本机替换，绝不在对话中提供真实密钥。

请解释改了哪些字段，如何分别保存到 Windows 和 Android。
提醒我核对游戏版本/MessagePack 限制，并先备份原存档。
我会附上脱敏后的当前配置（不含真实密钥、私有服务器地址或个人路径）。
```

不要把配置、日志、迁移包、同步状态或云备份索引直接公开；它们可能含使用者后来填入的私人信息。
