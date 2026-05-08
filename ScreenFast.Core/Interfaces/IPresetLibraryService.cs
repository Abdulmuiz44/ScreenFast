using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IPresetLibraryService
{
    ScreenFastPresetLibrary NormalizePresets(ScreenFastPresetLibrary? presets);

    ExportProfileLibrary NormalizeExportProfiles(ExportProfileLibrary? profiles);

    ScreenFastPresetSelection NormalizeSelection(ScreenFastPresetSelection? selection, ScreenFastPresetLibrary presets, ExportProfileLibrary profiles);
}
