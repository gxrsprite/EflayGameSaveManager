using System.Text.Json.Serialization;

namespace EflayGameSaveManager.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<SaveUnitType>))]
public enum SaveUnitType
{
    Folder,
    File,
    WinRegistry
}
