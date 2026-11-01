using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Maui.Platforms.Android;

namespace EflayGameSaveManager.Maui.Services;

public sealed record MobileNfsProfileChoice(string SavePath, string ProfileName);

public sealed class MobileNfsMwSaveService
{
    private readonly NfsMwPersonalSaveEditor _editor = new();

    public async Task<IReadOnlyList<MobileNfsProfileChoice>> ListAsync(IEnumerable<string> paths)
    {
        var result = new List<MobileNfsProfileChoice>();
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            var temporaryRoot = CreateTempRoot();
            try
            {
                var staged = await StageAsync(path, temporaryRoot);
                foreach (var profile in _editor.ListProfiles(staged))
                {
                    var original = staged == path ? profile.SavePath : Directory.Exists(staged)
                        ? Path.Combine(path, Path.GetRelativePath(staged, profile.SavePath)) : path;
                    result.Add(new MobileNfsProfileChoice(original, profile.ProfileName));
                }
            }
            finally { Directory.Delete(temporaryRoot, true); }
        }
        return result.DistinctBy(item => item.SavePath).OrderBy(item => item.ProfileName).ToArray();
    }

    public async Task<NfsMwPersonalProfile> ReadAsync(string path) => await ProcessAsync(path, p => _editor.Load(p), false);
    public async Task<NfsMwPersonalProfile> SaveAsync(string path, string alias, uint money, ulong bounty) =>
        await ProcessAsync(path, p => _editor.Save(p, alias, money, bounty), true);
    public async Task<NfsMwPersonalProfile> ApplyBossReadyAsync(string path) =>
        await ProcessAsync(path, p => _editor.ApplyBossFightReady(p), true);

    private static string CreateTempRoot()
    {
        var cache = global::Android.App.Application.Context.ExternalCacheDir?.AbsolutePath ?? FileSystem.Current.CacheDirectory;
        var root = Path.Combine(cache, "nfs9-edit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> StageAsync(string path, string root)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path;
        return await ShizukuFileService.CopyFromRestrictedAsync(path, root)
            ?? throw new IOException($"无法读取存档路径：{path}。请检查路径及 Shizuku 授权。");
    }

    private static async Task<NfsMwPersonalProfile> ProcessAsync(string path, Func<string, NfsMwPersonalProfile> action, bool write)
    {
        var root = CreateTempRoot();
        try
        {
            var staged = await StageAsync(path, root);
            var profile = action(staged);
            if (write && staged != path)
            {
                var backup = Directory.GetFiles(root, Path.GetFileName(staged) + ".bak_*").Single();
                var suffix = Path.GetFileName(backup)[Path.GetFileName(staged).Length..];
                if (!await ShizukuFileService.CopyToRestrictedAsync(backup, path + suffix))
                    throw new IOException("无法在原存档目录创建备份，未写回修改。");
                if (!await ShizukuFileService.CopyToRestrictedAsync(staged, path))
                    throw new IOException("写回存档失败，原存档备份已保留在原目录。");
            }
            return profile with { SavePath = path };
        }
        finally { Directory.Delete(root, true); }
    }
}
