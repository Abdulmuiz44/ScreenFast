using Microsoft.UI.Xaml;
using ScreenFast.App.ViewModels;
using WinRT.Interop;

namespace ScreenFast.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        Title = "ScreenFast";
        ViewModel = viewModel;
        ViewModel.InitializeWindowHandle(WindowNative.GetWindowHandle(this));
    }

    public MainWindowViewModel ViewModel { get; }
}
