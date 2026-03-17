using Microsoft.UI.Xaml;
using ScreenFast.App.Services;
using ScreenFast.App.ViewModels;
using WinRT.Interop;

namespace ScreenFast.App;

public sealed partial class MainWindow : Window
{
    private readonly IDesktopShellService _desktopShellService;

    public MainWindow(MainWindowViewModel viewModel, IDesktopShellService desktopShellService)
    {
        InitializeComponent();
        Title = "ScreenFast";
        ViewModel = viewModel;
        _desktopShellService = desktopShellService;
        _desktopShellService.MessageChanged += OnShellMessageChanged;

        var windowHandle = WindowNative.GetWindowHandle(this);
        ViewModel.InitializeWindowHandle(windowHandle);
        _desktopShellService.Initialize(windowHandle);
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        await ViewModel.InitializeAsync();
    }

    private void OnShellMessageChanged(object? sender, string? message)
    {
        ViewModel.SetShellMessage(message);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _desktopShellService.MessageChanged -= OnShellMessageChanged;
        _desktopShellService.Dispose();
    }
}
