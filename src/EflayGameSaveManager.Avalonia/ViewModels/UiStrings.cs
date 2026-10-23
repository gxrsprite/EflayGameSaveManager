using System;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Avalonia.ViewModels;

public sealed class UiStrings
{
    private UiStrings(bool english)
    {
        WindowTitle = "Eflay Game Save Manager";
        AppName = "Eflay Game Save Manager";
        ToggleInfo = english ? "Toggle Info" : "展开信息";
        RefreshCloud = english ? "Refresh Cloud" : "刷新云端";
        BackupAllCloud = english ? "Backup All To Cloud" : "全部备份到云端";
        BackupAllSaves = english ? "Export All Saves" : "导出全部存档";
        RestoreAllSaves = english ? "Restore All Saves" : "恢复全部存档";
        Reload = english ? "Reload" : "重新加载";
        Games = english ? "Games" : "游戏";
        AddGame = english ? "Add Game" : "添加游戏";
        Settings = english ? "Settings" : "设置";
        AddGameHint = english ? "Name, executable, and save units are edited on the right." : "游戏名称、可执行文件和存档单元在右侧编辑。";
        AddFolderSave = english ? "Add Folder Save" : "添加文件夹存档";
        AddFileSave = english ? "Add File Save" : "添加文件存档";
        AddRegistrySave = english ? "Add Registry Save" : "添加注册表存档";
        AddZipSave = english ? "Add Zip Save" : "添加 Zip 存档";
        ResetForm = english ? "Reset Form" : "重置表单";
        SyncCurrentSave = english ? "Sync Current Save To Cloud" : "同步当前存档到云端";
        RestoreFromCloud = english ? "Restore From Cloud" : "从云端恢复";
        RestoreLatestFromCloud = english ? "Restore Latest Cloud Save" : "恢复云端最新存档";
        RunGame = english ? "Run Game" : "启动游戏";
        OpenGameFolder = english ? "Open Game Folder" : "打开游戏目录";
        SaveDeviceSettings = english ? "Save Current Device Settings" : "保存当前设备设置";
        SavePaths = english ? "Save Paths" : "存档路径";
        Executables = english ? "Executables" : "可执行文件";
        CurrentGameExecutable = english ? "Current Device Game Executable" : "当前设备游戏可执行文件";
        CurrentGameExecutableHint = english ? "Enter current device game executable path" : "输入当前设备游戏可执行文件完整路径";
        OpenFolder = english ? "Open Folder" : "打开目录";
        SelectPath = english ? "Select Path" : "选择路径";
        ExternalLaunchers = english ? "External Launchers" : "外部启动程序";
        ExternalLauncherHint = english ? "Full paths for this device. Use categories such as Modifier, Launcher, or Tool." : "当前设备的完整路径。可使用修改器、启动器或工具等分类。";
        AddExternalProgram = english ? "Add External Program" : "添加外部程序";
        CategoryHint = english ? "Category (for example Modifier)" : "分类（例如：修改器）";
        ExternalProgramPathHint = english ? "Full path to the external program" : "外部程序完整路径";
        Run = english ? "Run" : "启动";
        Remove = english ? "Remove" : "移除";
        RebuildIndex = english ? "Rebuild Index" : "重建索引";
        DownloadZip = english ? "Download Zip" : "下载 Zip";
        RestoreSelected = english ? "Restore Selected" : "恢复所选";
        DeleteSelected = english ? "Delete Selected" : "删除所选";
        CurrentDeviceSavePaths = english ? "Current Device Save Paths" : "当前设备存档路径";
        CurrentDeviceSavePathHint = english ? "Enter current device save path" : "输入当前设备存档路径";
        BackupLocal = english ? "Backup Local" : "本地备份";
        AddGameDescription = english ? "Create a game entry for the current device. Paths can use tokens or absolute paths." : "为当前设备创建游戏条目。路径可使用令牌或绝对路径。";
        Game = english ? "Game" : "游戏";
        GameName = english ? "Game name" : "游戏名称";
        OptionalGameExecutable = english ? "Optional game executable path" : "可选游戏可执行文件路径";
        Folder = english ? "Folder" : "文件夹";
        File = english ? "File" : "文件";
        Registry = english ? "Registry" : "注册表";
        PathTokens = english ? "Path Tokens" : "路径令牌";
        CreateGame = english ? "Create Game" : "创建游戏";
        Reset = english ? "Reset" : "重置";
        BackToGames = english ? "Back To Games" : "返回游戏列表";
        InterfaceLanguage = english ? "Interface Language" : "界面语言";
        InterfaceLanguageHint = english ? "Switch immediately. The selection is saved to GameSaveManager.runtime.json." : "切换后立即生效，选择会保存到 GameSaveManager.runtime.json。";
        S3Storage = english ? "S3 Storage" : "S3 存储";
        Endpoint = english ? "Endpoint" : "服务地址";
        Bucket = english ? "Bucket" : "存储桶";
        Region = english ? "Region" : "区域";
        RootPath = english ? "Root Path" : "根路径";
        AccessKeyId = english ? "Access Key ID" : "访问密钥 ID";
        SecretAccessKey = english ? "Secret Access Key" : "访问密钥";
        SyncOptions = english ? "Sync Options" : "同步选项";
        MaxConcurrency = english ? "Max Concurrency" : "最大并发数";
        AutoSyncInterval = english ? "Auto Sync Interval" : "自动同步间隔";
        AlwaysSync = english ? "Always Sync" : "始终同步";
        SaveS3Settings = english ? "Save S3 Settings" : "保存 S3 设置";
        LanguageSaved = english ? "Interface language saved." : "界面语言已保存。";
        SelectGame = english ? "Select a game" : "请选择游戏";
        GameChineseName = english ? "Chinese name" : "中文名";
        GameNote = english ? "Note" : "备注";
        GameInfoHint = english
            ? "Chinese name and note are saved together with “Save Current Device Settings”. In Chinese UI the list and title prefer the Chinese name."
            : "中文名与备注会随“保存当前设备设置”一起保存；中文界面下列表与标题优先显示中文名。";
        ExcludePatterns = english ? "Exclude patterns" : "排除规则";
        ExcludePatternsHint = english
            ? "Semicolon-separated wildcards for device-local files, e.g. settings*; Prefs.dict. Matching files are never packed on backup and never overwritten on restore."
            : "分号分隔的通配符，用于设备本地文件，例如 settings*; Prefs.dict。匹配的文件备份时不打包、恢复时不覆盖。";
        ExcludePatternsWatermark = english ? "e.g. settings*; Prefs.dict" : "例如 settings*; Prefs.dict";
        ContentsOnly = english ? "Sync folder contents only" : "只同步文件夹内容";
        ContentsOnlyHint = english
            ? "Ignores the folder's own name so devices with different directory names still line up (e.g. PC \"saves\" vs Android \"user\")."
            : "忽略文件夹自身名称，两边目录名不同也能对齐（例如 PC 的 saves 与手机的 user）。";
    }

    public string WindowTitle { get; }
    public string AppName { get; }
    public string ToggleInfo { get; }
    public string RefreshCloud { get; }
    public string BackupAllCloud { get; }
    public string BackupAllSaves { get; }
    public string RestoreAllSaves { get; }
    public string Reload { get; }
    public string Games { get; }
    public string AddGame { get; }
    public string Settings { get; }
    public string AddGameHint { get; }
    public string AddFolderSave { get; }
    public string AddFileSave { get; }
    public string AddRegistrySave { get; }
    public string AddZipSave { get; }
    public string ResetForm { get; }
    public string SyncCurrentSave { get; }
    public string RestoreFromCloud { get; }
    public string RestoreLatestFromCloud { get; }
    public string RunGame { get; }
    public string OpenGameFolder { get; }
    public string SaveDeviceSettings { get; }
    public string SavePaths { get; }
    public string Executables { get; }
    public string CurrentGameExecutable { get; }
    public string CurrentGameExecutableHint { get; }
    public string OpenFolder { get; }
    public string SelectPath { get; }
    public string ExternalLaunchers { get; }
    public string ExternalLauncherHint { get; }
    public string AddExternalProgram { get; }
    public string CategoryHint { get; }
    public string ExternalProgramPathHint { get; }
    public string Run { get; }
    public string Remove { get; }
    public string RebuildIndex { get; }
    public string DownloadZip { get; }
    public string RestoreSelected { get; }
    public string DeleteSelected { get; }
    public string CurrentDeviceSavePaths { get; }
    public string CurrentDeviceSavePathHint { get; }
    public string BackupLocal { get; }
    public string AddGameDescription { get; }
    public string Game { get; }
    public string GameName { get; }
    public string OptionalGameExecutable { get; }
    public string Folder { get; }
    public string File { get; }
    public string Registry { get; }
    public string PathTokens { get; }
    public string CreateGame { get; }
    public string Reset { get; }
    public string BackToGames { get; }
    public string InterfaceLanguage { get; }
    public string InterfaceLanguageHint { get; }
    public string S3Storage { get; }
    public string Endpoint { get; }
    public string Bucket { get; }
    public string Region { get; }
    public string RootPath { get; }
    public string AccessKeyId { get; }
    public string SecretAccessKey { get; }
    public string SyncOptions { get; }
    public string MaxConcurrency { get; }
    public string AutoSyncInterval { get; }
    public string AlwaysSync { get; }
    public string SaveS3Settings { get; }
    public string LanguageSaved { get; }
    public string SelectGame { get; }
    public string GameChineseName { get; }
    public string GameNote { get; }
    public string GameInfoHint { get; }
    public string ExcludePatterns { get; }
    public string ExcludePatternsHint { get; }
    public string ExcludePatternsWatermark { get; }
    public string ContentsOnly { get; }
    public string ContentsOnlyHint { get; }

    public static UiStrings Create(string language) =>
        new(string.Equals(language, AppLanguage.English, StringComparison.OrdinalIgnoreCase));
}

public sealed class LanguageOptionViewModel
{
    public LanguageOptionViewModel(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public string Code { get; }

    public string DisplayName { get; }
}
