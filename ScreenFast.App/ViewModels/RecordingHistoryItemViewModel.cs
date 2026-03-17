using System.Collections.Generic;
using ScreenFast.Core.Models;

namespace ScreenFast.App.ViewModels;

public sealed class RecordingHistoryItemViewModel
{
    public RecordingHistoryItemViewModel(RecordingHistoryEntry model)
    {
        Model = model;
    }

    public RecordingHistoryEntry Model { get; }

    public Guid Id => Model.Id;

    public string FileName => string.IsNullOrWhiteSpace(Model.FileName) ? "(missing file name)" : Model.FileName;

    public string FilePath => Model.FilePath;

    public string CreatedAtText => Model.CreatedAt.LocalDateTime.ToString("g");

    public string DurationText => Model.Duration.ToString(@"hh\:mm\:ss");

    public string SourceSummary => Model.SourceSummary;

    public string QualityPreset => Model.QualityPreset;

    public bool IncludedSystemAudio => Model.IncludedSystemAudio;

    public bool IncludedMicrophone => Model.IncludedMicrophone;

    public bool IsFileAvailable => Model.IsFileAvailable;

    public bool IsSuccess => Model.IsSuccess;

    public string MediaFlagsText
    {
        get
        {
            var flags = new List<string>();
            if (IncludedSystemAudio) flags.Add("System audio");
            if (IncludedMicrophone) flags.Add("Mic");
            return flags.Count == 0 ? "Silent" : string.Join(" + ", flags);
        }
    }

    public string AvailabilityText => Model.IsSuccess
        ? (Model.IsFileAvailable ? "Available" : "Missing")
        : "Failed";

    public string FailureText => Model.FailureSummary ?? string.Empty;

    public string FileSizeText => Model.FileSizeBytes.HasValue
        ? FormatFileSize(Model.FileSizeBytes.Value)
        : "";

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var index = 0;
        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.#} {suffixes[index]}";
    }
}
