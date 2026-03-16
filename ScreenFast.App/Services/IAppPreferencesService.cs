using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public interface IAppPreferencesService
{
    event EventHandler<AppSettings>? SettingsChanged;

    AppSettings CurrentSettings { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateHotkeySettingsAsync(HotkeySettings hotkeys, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateTrayBehaviorAsync(bool launchMinimizedToTray, bool closeToTray, bool minimizeToTray, CancellationToken cancellationToken = default);
}
