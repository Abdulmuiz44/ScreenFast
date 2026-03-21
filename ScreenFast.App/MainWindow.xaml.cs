using Microsoft.UI.Xaml;
using ScreenFast.App.Services;
using ScreenFast.App.ViewModels;
using WinRT.Interop;

namespace ScreenFast.App;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    private readonly IDesktopShellService _desktopShellService;
    private readonly IRecordingIndicatorOverlayService _overlayService;

    public MainWindow(MainWindowViewModel viewModel, IDesktopShellService desktopShellService, IRecordingIndicatorOverlayService overlayService)
    {
        InitializeComponent();
        Title = "ScreenFast";
        ViewModel = viewModel;
        _desktopShellService = desktopShellService;
        _overlayService = overlayService;
        _desktopShellService.MessageChanged += OnShellMessageChanged;

        var windowHandle = WindowNative.GetWindowHandle(this);
        ViewModel.InitializeWindowHandle(windowHandle);
        _desktopShellService.Initialize(windowHandle);
        _overlayService.Initialize(windowHandle);
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
        _overlayService.Dispose();
    }
}


