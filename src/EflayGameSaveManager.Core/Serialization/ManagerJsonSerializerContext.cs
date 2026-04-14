using System.Text.Json.Serialization;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Serialization;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ManagerConfig))]
[JsonSerializable(typeof(AppRuntimeSettings))]
[JsonSerializable(typeof(GameCloudManifest))]
[JsonSerializable(typeof(BackupBatchManifest))]
[JsonSerializable(typeof(LegacyGameBackups))]
[JsonSerializable(typeof(LegacySyncState))]
internal partial class ManagerJsonSerializerContext : JsonSerializerContext
{
}
