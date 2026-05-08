using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class PresetLibraryService : IPresetLibraryService
{
    public ScreenFastPresetLibrary NormalizePresets(ScreenFastPresetLibrary? presets)
    {
        var defaults = ScreenFastPresetLibrary.CreateDefault();
        if (presets is null)
        {
            return defaults;
        }

        return new ScreenFastPresetLibrary(
            Math.Max(1, presets.Version),
            MergeById(presets.RecordingPresets, defaults.RecordingPresets, x => x.Id),
            MergeById(presets.ZoomPresets, defaults.ZoomPresets, x => x.Id),
            MergeById(presets.StylingPresets, defaults.StylingPresets, x => x.Id),
            MergeById(presets.ExportPresets, defaults.ExportPresets, x => x.Id));
    }

    public ExportProfileLibrary NormalizeExportProfiles(ExportProfileLibrary? profiles)
    {
        var defaults = ExportProfileLibrary.CreateDefault();
        if (profiles is null)
        {
            return defaults;
        }

        return new ExportProfileLibrary(
            Math.Max(1, profiles.Version),
            MergeById(profiles.Profiles, defaults.Profiles, x => x.Id));
    }

    public ScreenFastPresetSelection NormalizeSelection(ScreenFastPresetSelection? selection, ScreenFastPresetLibrary presets, ExportProfileLibrary profiles)
    {
        var fallback = ScreenFastPresetSelection.CreateDefault();
        selection ??= fallback;

        var recordingId = presets.RecordingPresets.Any(x => x.Id == selection.RecordingPresetId) ? selection.RecordingPresetId : fallback.RecordingPresetId;
        var exportPresetId = presets.ExportPresets.Any(x => x.Id == selection.ExportPresetId) ? selection.ExportPresetId : fallback.ExportPresetId;
        var exportProfileId = profiles.Profiles.Any(x => x.Id == selection.ExportProfileId) ? selection.ExportProfileId : fallback.ExportProfileId;
        var zoomId = presets.ZoomPresets.Any(x => x.Id == selection.ZoomPresetId) ? selection.ZoomPresetId : fallback.ZoomPresetId;
        var stylingId = presets.StylingPresets.Any(x => x.Id == selection.StylingPresetId) ? selection.StylingPresetId : fallback.StylingPresetId;

        return new ScreenFastPresetSelection(recordingId, zoomId, stylingId, exportPresetId, exportProfileId);
    }

    private static List<T> MergeById<T>(IEnumerable<T>? configured, IEnumerable<T> defaults, Func<T, string> idSelector)
    {
        var result = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in configured ?? [])
        {
            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(item);
        }

        foreach (var item in defaults)
        {
            if (seen.Add(idSelector(item)))
            {
                result.Add(item);
            }
        }

        return result;
    }
}
