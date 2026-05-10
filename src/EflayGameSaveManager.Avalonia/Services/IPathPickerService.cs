using System.Threading.Tasks;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Avalonia.Services;

public interface IPathPickerService
{
    Task<string?> PickGameExecutablePathAsync(string currentPath);

    Task<string?> PickSavePathAsync(SaveUnitType unitType, string currentPath);
}
