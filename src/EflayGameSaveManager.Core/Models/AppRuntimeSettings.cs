using System.Text.Json.Serialization;

namespace EflayGameSaveManager.Core.Models;

public sealed class AppRuntimeSettings
{
    [JsonPropertyName("forced_device_name")]
    public string ForcedDeviceName { get; set; } = string.Empty;
}
