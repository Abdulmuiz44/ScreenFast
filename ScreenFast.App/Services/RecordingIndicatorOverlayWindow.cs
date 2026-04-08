using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenFast.App.Interop;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenFast.App.Services;

internal sealed class RecordingIndicatorOverlayWindow : Window
{
    private readonly Border _panel;
    private readonly TextBlock _stateText;
    private readonly TextBlock _timerText;

    public RecordingIndicatorOverlayWindow()
    {
        ExtendsContentIntoTitleBar = true;
        _stateText = new TextBlock
        {
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Text = "Recording"
        };
        _timerText = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Text = "00:00:00"
        };
        _panel = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 24, 24, 24)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _stateText,
                    _timerText
                }
            }
        };

        Content = _panel;
    }

    public void Configure(nint ownerWindowHandle)
    {
        var handle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetPresenter(OverlappedPresenter.CreateForToolWindow());
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        var displayArea = DisplayArea.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerWindowHandle), DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        appWindow.MoveAndResize(new RectInt32(workArea.X + workArea.Width - 240, workArea.Y + 20, 220, 84));

        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style));
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopMost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public void Update(string stateLabel, string timerText)
    {
        _stateText.Text = stateLabel;
        _timerText.Text = timerText;
        _panel.Background = new SolidColorBrush(stateLabel == "Paused"
            ? Microsoft.UI.ColorHelper.FromArgb(220, 120, 88, 16)
            : Microsoft.UI.ColorHelper.FromArgb(220, 156, 28, 28));
    }
}
