using Avalonia.Controls;
using Avalonia.Controls.Templates;
using EflayGameSaveManager.Avalonia.ViewModels;
using EflayGameSaveManager.Avalonia.Views;

namespace EflayGameSaveManager.Avalonia;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            MainWindowViewModel => new MainWindow(),
            null => null,
            _ => new TextBlock { Text = $"Not Found: {param.GetType().FullName}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
