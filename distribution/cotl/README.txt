咩咩启示录存档助手 · Windows + Android 公众发布包

先完整解压 RAR，再运行 Windows-x64\GameSaveManager.exe。
Android-arm64\CultOfTheLamb-SaveManager.apk 是存档助手，不是游戏安装包。
Windows 版已包含 .NET 运行时。Android 版要求 Android 7.0+、arm64 设备。

本包只有《咩咩启示录》这一项游戏、PC 和 Android 两个通用设备。
没有附带任何人的存档、设备 GUID、MinIO 服务地址或云存储凭据。
默认云同步关闭；使用者必须填写自己的 S3 兼容存储，双方使用同一 bucket/root_path。
PC 版可先使用本地备份；当前 Android 界面主要提供云备份/恢复，不包含独立本地备份按钮。

快速开始：
1. 关闭两端游戏，分别手动复制一份原始存档，留作恢复依据。
2. Windows 默认路径：
   %USERPROFILE%\AppData\LocalLow\Massive Monster\Cult Of The Lamb\saves
   模板使用 <winLocalAppDataLow> 代替用户名，通常无需修改。
3. Android 默认示例路径：
   /storage/emulated/0/Android/data/com.cotl.port/files/user
   该路径只适用于对应移植版。其他包名/版本必须按实际目录修改。
4. Android 沿用原助手包名 com.eflay.gamesavemanager.maui，
   只是预装《咩咩启示录》脱敏配置，不是另一个独立应用。
   首次安装（或清除本助手应用数据后）启动会自动写入本包预设。
   覆盖安装会保留手机上原来的配置；若要换成此预设，请在 Config 页面
   粘贴本包 Android-arm64\GameSaveManager.config.json 的内容并保存。
   若原 runtime 的设备名不是 Android，还需让配置设备名与其保持一致，
   或先备份配置，再清除本助手应用数据重新启动以同时初始化两个 JSON。
   若读取受限目录，请安装、启动 Shizuku 并授权本助手。
   Shizuku 的 ADB 权限不等于 root，不能保证读取其他应用 /data/data 私有目录。
5. 按 CONFIG_GUIDE.md 在两端填写自己的云配置，并把 cloud_sync_enabled 改为 true。
   Android：Menu → Config，Reload 后编辑 JSON，再点 Save。
   Windows：关闭程序编辑同目录的 GameSaveManager.config.json，保存后重启；
   S3 字段也可在桌面“设置”页面填写。
6. 在当前进度所在设备点备份/Upload current，成功后再在另一设备恢复/Restore latest。
   Android Restore latest 会选云端最新备份，不会自动合并进度。确认备份时间再操作。

版本限制：
旧版 PC 游戏不写保存日期时，安卓存档列表可能显示 0001 年。
本助手在备份和恢复时使用来源文件时间补齐缺失/无效日期，已有有效日期保持原样。
补齐日期不会修改游戏天数或游玩进度；历史文件时间只能反映文件记录的保存/复制时间。
支持的互转格式是旧版 PC AES/JSON 与 com.cotl.port Android ZB/gzip/JSON。
PC 1.5.x+ 的 *.mp / MessagePack 存档尚未实现与 Android JSON 的互转。
遇到此格式会拒绝相关转换；不要把 .mp 文件直接改后缀伪装成 JSON。
游戏本身的版本/DLC/模组差异仍可能导致存档不兼容。
如需自动检测 PC 游戏版本，可在配置 game_paths.pc 填写自己的游戏 EXE 路径。

设备说明：
pc / android 是公开模板里的逻辑设备键，不是作者的 GUID，也不是硬件标识。
Windows runtime 固定选择 PC，Android runtime 固定选择 Android。
同一云空间只建议一台 PC + 一台手机；多设备需要另建 ID 并同步修改引用。

配置文件说明及可直接复制给 AI 的提示词见 CONFIG_GUIDE.md。
Windows/Android 两个目录都附有脱敏配置；Android 目录的 JSON 是 APK 内置模板的副本，
修改 APK 旁边的文件不会自动改变已安装应用，应使用应用内 Config 编辑。
安装更新保留旧配置；卸载或清除数据前请备份自己的配置和存档。

此包不包含游戏或 Shizuku。APK 由本地构建签名，用于直接安装，非应用商店官方发行。
SHA256SUMS.txt 可用于校验文件完整性，不代表公开信任证书。
