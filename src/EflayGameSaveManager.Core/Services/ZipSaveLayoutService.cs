using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EflayGameSaveManager.Core.Services;

/// <summary>One folder/content contract for ZIP payloads and Folder archives.</summary>
public static class ZipSaveLayoutService
{
    private const string ArchiveHeader = "RGSM_ARCHIVE_V2\n";
    private const string PayloadHeader = "RGSM_ZIP_PAYLOAD_V1\n";
    internal sealed record UnitLayout(int Id, string? FolderName, bool ContentsOnly);
    private sealed record Payload(string Root, string? FolderName);

    public static string CreateUploadArchive(string inputZip, string workingDirectory, int unitId, bool contentsOnly)
    {
        var work = Path.Combine(workingDirectory, "zip-layout-" + Guid.NewGuid().ToString("N"));
        var payload = ReadPayload(inputZip, work, unitId, legacyUnitWrapper: false);
        if (!Directory.EnumerateFiles(payload.Root, "*", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("ZIP 中没有可备份的存档文件。");
        var unitRoot = Path.Combine(work, "output", unitId.ToString());
        var destination = contentsOnly ? Path.Combine(unitRoot, "content") :
            payload.FolderName is { } name ? Path.Combine(unitRoot, name) : unitRoot;
        Copy(payload.Root, destination);
        var result = Path.Combine(work, "upload.zip");
        ZipFile.CreateFromDirectory(Path.Combine(work, "output"), result, CompressionLevel.Optimal, false);
        WriteArchiveMetadata(result, [new(unitId, payload.FolderName, contentsOnly)]);
        return result;
    }

    public static string CreateRestoreZip(string inputArchive, string workingDirectory, bool contentsOnly,
        int? unitId = null, string? fallbackFolderName = null)
    {
        var work = Path.Combine(workingDirectory, "zip-layout-" + Guid.NewGuid().ToString("N"));
        var payload = ReadPayload(inputArchive, work, unitId, legacyUnitWrapper: true);
        if (!Directory.EnumerateFiles(payload.Root, "*", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("ZIP 中没有可恢复的存档文件。");
        var output = Path.Combine(work, "output");
        var folderName = payload.FolderName;
        // Older contents-only archives did not retain the original basename.
        if (!contentsOnly && folderName is null && IsContentsArchive(inputArchive))
            folderName = SafeName(fallbackFolderName);
        Copy(payload.Root, !contentsOnly && folderName is not null ? Path.Combine(output, folderName) : output);
        var result = Path.Combine(work, "restore.zip");
        ZipFile.CreateFromDirectory(output, result, CompressionLevel.Optimal, false);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("folder_name", folderName);
            writer.WriteBoolean("contents_only", contentsOnly);
            writer.WriteEndObject();
        }
        using (var zip = ZipFile.Open(result, ZipArchiveMode.Update))
            zip.Comment = PayloadHeader + Encoding.UTF8.GetString(stream.ToArray());
        return result;
    }

    internal static void WriteArchiveMetadata(string path, IEnumerable<UnitLayout> units)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 2);
            writer.WriteString("compression", "deflate");
            writer.WriteStartObject("units");
            foreach (var unit in units)
            {
                writer.WriteStartObject(unit.Id.ToString());
                writer.WriteString("folder_name", SafeName(unit.FolderName));
                writer.WriteBoolean("contents_only", unit.ContentsOnly);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.Comment = ArchiveHeader + Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static bool? ReadUnitContentsOnly(string archivePath, string unitId)
    {
        string comment;
        try { using var zip = ZipFile.OpenRead(archivePath); comment = zip.Comment; }
        catch (InvalidDataException) { return null; }
        if (!comment.StartsWith(ArchiveHeader, StringComparison.Ordinal)) return null;
        using var document = JsonDocument.Parse(comment[ArchiveHeader.Length..]);
        return document.RootElement.TryGetProperty("units", out var units) && units.TryGetProperty(unitId, out var unit)
            ? unit.GetProperty("contents_only").GetBoolean() : null;
    }

    private static Payload ReadPayload(string archivePath, string work, int? unitId, bool legacyUnitWrapper)
    {
        string comment;
        using (var archive = ZipFile.OpenRead(archivePath)) comment = archive.Comment;
        var extractRoot = Path.Combine(work, "extract");
        Directory.CreateDirectory(extractRoot);
        ArchiveTransferService.ExtractArchive(archivePath, extractRoot);
        if (comment.StartsWith(PayloadHeader, StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(comment[PayloadHeader.Length..]);
            var name = ReadName(document.RootElement);
            var isContents = document.RootElement.GetProperty("contents_only").GetBoolean();
            // A restored contents ZIP may legitimately have a single numeric directory.
            // Do not mistake it for a wrapper and strip it again on the next upload.
            return new Payload(!isContents && name is not null ? RequiredChild(extractRoot, name) : extractRoot, name);
        }

        var directories = Directory.GetDirectories(extractRoot);
        var files = Directory.GetFiles(extractRoot);
        var managed = comment.StartsWith(ArchiveHeader, StringComparison.Ordinal);
        string? unitRoot = null;
        if (managed && unitId is { } id && Directory.Exists(Path.Combine(extractRoot, id.ToString())))
            unitRoot = Path.Combine(extractRoot, id.ToString());
        else if (files.Length == 0 && directories.Length == 1 &&
            (managed && int.TryParse(Path.GetFileName(directories[0]), out _) ||
             legacyUnitWrapper && Path.GetFileName(directories[0]) == "0"))
            unitRoot = directories[0];
        if (managed && unitRoot is null)
            throw new InvalidDataException("备份包含多个存档单元，无法确定此 ZIP 对应的单元，请检查配置中的单元 ID。");

        if (unitRoot is not null)
        {
            string? name = null;
            bool? sourceContents = null;
            if (managed)
            {
                using var document = JsonDocument.Parse(comment[ArchiveHeader.Length..]);
                if (document.RootElement.TryGetProperty("units", out var units) &&
                    units.TryGetProperty(Path.GetFileName(unitRoot), out var metadata))
                {
                    name = ReadName(metadata);
                    sourceContents = metadata.GetProperty("contents_only").GetBoolean();
                }
            }
            if (sourceContents == true || sourceContents is null && Directory.Exists(Path.Combine(unitRoot, "content")))
                return new Payload(RequiredChild(unitRoot, "content"), name);
            if (name is not null) return new Payload(RequiredChild(unitRoot, name), name);
            if (sourceContents == false) return new Payload(unitRoot, null);
            return InferOuterFolder(unitRoot);
        }
        return InferOuterFolder(extractRoot);
    }

    private static Payload InferOuterFolder(string root)
    {
        var directories = Directory.GetDirectories(root);
        return Directory.GetFiles(root).Length == 0 && directories.Length == 1
            ? new Payload(directories[0], Path.GetFileName(directories[0]))
            : new Payload(root, null);
    }

    private static string RequiredChild(string root, string name)
    {
        var path = Path.Combine(root, name);
        if (!Directory.Exists(path)) throw new InvalidDataException("ZIP 目录结构与存档布局信息不一致。");
        return path;
    }

    private static string? ReadName(JsonElement element) =>
        element.TryGetProperty("folder_name", out var value) && value.ValueKind == JsonValueKind.String ? SafeName(value.GetString()) : null;

    private static string? SafeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value is "." or ".." || value.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("存档布局中的文件夹名称无效。");
        return value;
    }

    private static bool IsContentsArchive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Any(entry =>
        {
            var parts = entry.FullName.Split('/');
            return parts.Length > 2 && int.TryParse(parts[0], out _) && parts[1] == "content";
        });
    }

    private static void Copy(string source, string target) =>
        SavePathFilter.CopyDirectory(source, target, null, (from, to) => FileCopyHelper.CopyFile(from, to));
}
