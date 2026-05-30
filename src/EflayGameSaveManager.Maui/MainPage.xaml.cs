using EflayGameSaveManager.Maui.ViewModels;

namespace EflayGameSaveManager.Maui;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_viewModel.HasLoaded)
        {
            await _viewModel.InitializeAsync();
        }
    }
}
