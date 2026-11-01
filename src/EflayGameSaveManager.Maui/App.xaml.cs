using Microsoft.Extensions.DependencyInjection;

namespace EflayGameSaveManager.Maui;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_serviceProvider.GetRequiredService<MainPage>());
    }
}
