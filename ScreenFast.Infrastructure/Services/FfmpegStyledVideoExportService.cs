using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class FfmpegStyledVideoExportService : IStyledVideoExportService
{
    private const int SupportedStyledExportSchemaVersion = 1;
    private const string FfmpegDownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    private const string CachedFfmpegRelativePath = "ffmpeg-master-latest-win64-gpl\\bin\\ffmpeg.exe";

    private static readonly SemaphoreSlim FfmpegAcquireGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IScreenFastLogService _logService;

    public FfmpegStyledVideoExportService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public async Task<OperationResult<StyledVideoExportResult>> ExportAsync(
        string styledExportPlanPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(styledExportPlanPath) || !File.Exists(styledExportPlanPath))
        {
            return OperationResult<StyledVideoExportResult>.Failure(
                AppError.SourceUnavailable("Styled export plan file was not found."));
        }

        var ffmpegPathResult = await ResolveOrAcquireFfmpegPathAsync(cancellationToken);
        if (!ffmpegPathResult.IsSuccess || string.IsNullOrWhiteSpace(ffmpegPathResult.Value))
        {
            return OperationResult<StyledVideoExportResult>.Failure(ffmpegPathResult.Error!);
        }

        var ffmpegPath = ffmpegPathResult.Value;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return OperationResult<StyledVideoExportResult>.Failure(
                AppError.ShellActionFailed("ScreenFast could not prepare ffmpeg for styled auto-zoom MP4 exports."));
        }

        var planResult = await ReadPlanAsync(styledExportPlanPath, cancellationToken);
        if (!planResult.IsSuccess || planResult.Value is null)
        {
            return OperationResult<StyledVideoExportResult>.Failure(planResult.Error!);
        }

        var plan = planResult.Value;
        var validation = ValidatePlan(plan);
        if (!validation.IsSuccess)
        {
            return OperationResult<StyledVideoExportResult>.Failure(validation.Error!);
        }

        var outputPath = EnsureOutputPath(plan.SuggestedOutputVideoPath, plan.InputVideoPath);
        var tempOutputPath = outputPath + ".tmp.mp4";
        TryDelete(tempOutputPath);

        var warnings = plan.Diagnostics.Warnings.ToList();
        if (plan.Composition.Background.Kind == StyledExportBackgroundKind.LinearGradient)
        {
            warnings.Add("This renderer slice uses the primary background color for gradient presets.");
        }

        if (plan.Composition.FrameStyle.CornerRadiusPixels > 0)
        {
            warnings.Add("This renderer slice does not yet apply rounded video corners.");
        }

        if (plan.Composition.FrameStyle.ShadowKind != StyledExportShadowKind.None)
        {
            warnings.Add("This renderer slice does not yet apply frame shadows.");
        }

        var filterGraph = BuildFilterGraph(plan);
        var stdErr = new StringBuilder();
        try
        {
            var exitCode = await RunFfmpegAsync(
                ffmpegPath,
                plan.InputVideoPath,
                tempOutputPath,
                filterGraph,
                stdErr,
                cancellationToken);

            if (exitCode != 0)
            {
                TryDelete(tempOutputPath);
                var message = BuildFfmpegFailureMessage(stdErr);
                _logService.Warning(
                    "styled_video_export.ffmpeg_failed",
                    "ScreenFast could not render the styled auto-zoom MP4.",
                    new Dictionary<string, object?>
                    {
                        ["inputVideoPath"] = plan.InputVideoPath,
                        ["outputVideoPath"] = outputPath,
                        ["exitCode"] = exitCode,
                        ["error"] = message
                    });
                return OperationResult<StyledVideoExportResult>.Failure(AppError.ShellActionFailed(message));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Move(tempOutputPath, outputPath, true);

            var result = new StyledVideoExportResult(
                plan.InputVideoPath,
                outputPath,
                styledExportPlanPath,
                plan.TimelineSegments.Count,
                warnings.Distinct(StringComparer.Ordinal).ToArray());

            _logService.Info(
                "styled_video_export.completed",
                "ScreenFast rendered a styled auto-zoom MP4.",
                new Dictionary<string, object?>
                {
                    ["inputVideoPath"] = result.InputVideoPath,
                    ["outputVideoPath"] = result.OutputVideoPath,
                    ["styledExportPlanPath"] = result.StyledExportPlanPath,
                    ["segmentCount"] = result.SegmentCount,
                    ["warningCount"] = result.Warnings.Count
                });
            return OperationResult<StyledVideoExportResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempOutputPath);
            return OperationResult<StyledVideoExportResult>.Failure(AppError.ShellActionFailed("Styled export was cancelled."));
        }
        catch (Exception ex)
        {
            TryDelete(tempOutputPath);
            _logService.Warning(
                "styled_video_export.failed",
                "ScreenFast could not render the styled auto-zoom MP4.",
                new Dictionary<string, object?>
                {
                    ["inputVideoPath"] = plan.InputVideoPath,
                    ["outputVideoPath"] = outputPath,
                    ["error"] = ex.Message
                });
            return OperationResult<StyledVideoExportResult>.Failure(
                AppError.ShellActionFailed($"ScreenFast could not render the styled export: {ex.Message}"));
        }
    }

    private static async Task<OperationResult<StyledExportPlan>> ReadPlanAsync(
        string styledExportPlanPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(styledExportPlanPath);
            var plan = await JsonSerializer.DeserializeAsync<StyledExportPlan>(stream, JsonOptions, cancellationToken);
            return plan is null
                ? OperationResult<StyledExportPlan>.Failure(AppError.SourceUnavailable("Styled export plan file was empty or invalid."))
                : OperationResult<StyledExportPlan>.Success(plan);
        }
        catch (Exception ex)
        {
            return OperationResult<StyledExportPlan>.Failure(
                AppError.SourceUnavailable($"Styled export plan could not be read: {ex.Message}"));
        }
    }

    private static OperationResult ValidatePlan(StyledExportPlan plan)
    {
        if (plan.SchemaVersion != SupportedStyledExportSchemaVersion)
        {
            return OperationResult.Failure(
                AppError.SourceUnavailable($"Unsupported styled export plan schema version {plan.SchemaVersion}."));
        }

        if (string.IsNullOrWhiteSpace(plan.InputVideoPath) || !File.Exists(plan.InputVideoPath))
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Styled export input video was not found."));
        }

        if (plan.Composition.OutputWidth <= 0 || plan.Composition.OutputHeight <= 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Styled export output dimensions are invalid."));
        }

        if (plan.OutputContentRect.Width <= 0 || plan.OutputContentRect.Height <= 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Styled export content rectangle is invalid."));
        }

        if (plan.TimelineSegments.Count == 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Styled export plan contains no timeline segments."));
        }

        return OperationResult.Success();
    }

    private static string BuildFilterGraph(StyledExportPlan plan)
    {
        var outputWidth = MakeEven(plan.Composition.OutputWidth);
        var outputHeight = MakeEven(plan.Composition.OutputHeight);
        var contentWidth = MakeEven((int)Math.Round(plan.OutputContentRect.Width));
        var contentHeight = MakeEven((int)Math.Round(plan.OutputContentRect.Height));
        var contentX = (int)Math.Round(plan.OutputContentRect.X);
        var contentY = (int)Math.Round(plan.OutputContentRect.Y);
        var backgroundColor = ToFfmpegColor(plan.Composition.Background.PrimaryColor);
        var parts = new List<string>();
        var labels = new List<string>();

        for (var index = 0; index < plan.TimelineSegments.Count; index++)
        {
            var segment = plan.TimelineSegments[index];
            var duration = Math.Max(0.001, (segment.EndMilliseconds - segment.StartMilliseconds) / 1000d);
            var start = segment.StartMilliseconds / 1000d;
            var end = Math.Max(segment.EndMilliseconds / 1000d, start + 0.001);
            var viewport = NormalizeViewport(segment.SourceViewport, plan);
            var cropWidth = MakeEven((int)Math.Round(viewport.Width));
            var cropHeight = MakeEven((int)Math.Round(viewport.Height));
            var cropX = Math.Max(0, (int)Math.Round(viewport.X));
            var cropY = Math.Max(0, (int)Math.Round(viewport.Y));
            var label = $"seg{index}";

            parts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"color=c={backgroundColor}:s={outputWidth}x{outputHeight}:d={duration:F3}[bg{index}];" +
                    $"[0:v]trim=start={start:F3}:end={end:F3},setpts=PTS-STARTPTS,crop={cropWidth}:{cropHeight}:{cropX}:{cropY},scale={contentWidth}:{contentHeight}:flags=lanczos[fg{index}];" +
                    $"[bg{index}][fg{index}]overlay={contentX}:{contentY}:format=auto,format=yuv420p[{label}]"));
            labels.Add($"[{label}]");
        }

        parts.Add($"{string.Concat(labels)}concat=n={labels.Count}:v=1:a=0[outv]");
        return string.Join(";", parts);
    }

    private static CameraViewportRect NormalizeViewport(CameraViewportRect viewport, StyledExportPlan plan)
    {
        var width = Math.Clamp(viewport.Width, 2, plan.Diagnostics.OutputWidth * 8d);
        var height = Math.Clamp(viewport.Height, 2, plan.Diagnostics.OutputHeight * 8d);
        var x = Math.Max(0, viewport.X);
        var y = Math.Max(0, viewport.Y);
        return new CameraViewportRect(x, y, width, height);
    }

    private static async Task<int> RunFfmpegAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        string filterGraph,
        StringBuilder stdErr,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(filterGraph);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("[outv]");
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a?");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("veryfast");
        startInfo.ArgumentList.Add("-crf");
        startInfo.ArgumentList.Add("18");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");
        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add("192k");
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add("-shortest");
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                stdErr.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private async Task<OperationResult<string>> ResolveOrAcquireFfmpegPathAsync(CancellationToken cancellationToken)
    {
        var existing = ResolveFfmpegPath();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return OperationResult<string>.Success(existing);
        }

        await FfmpegAcquireGate.WaitAsync(cancellationToken);
        try
        {
            existing = ResolveFfmpegPath();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return OperationResult<string>.Success(existing);
            }

            _logService.Info(
                "styled_video_export.ffmpeg_download_started",
                "ScreenFast is downloading ffmpeg for styled auto-zoom exports.",
                new Dictionary<string, object?>
                {
                    ["url"] = FfmpegDownloadUrl,
                    ["targetFolder"] = ScreenFastPaths.FfmpegFolderPath
                });

            var downloaded = await DownloadFfmpegAsync(cancellationToken);
            _logService.Info(
                "styled_video_export.ffmpeg_download_completed",
                "ScreenFast downloaded ffmpeg for styled auto-zoom exports.",
                new Dictionary<string, object?> { ["ffmpegPath"] = downloaded });
            return OperationResult<string>.Success(downloaded);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Failure(AppError.ShellActionFailed("ffmpeg download was cancelled."));
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "styled_video_export.ffmpeg_download_failed",
                "ScreenFast could not download ffmpeg for styled auto-zoom exports.",
                new Dictionary<string, object?>
                {
                    ["url"] = FfmpegDownloadUrl,
                    ["error"] = ex.Message
                });
            return OperationResult<string>.Failure(
                AppError.ShellActionFailed($"ScreenFast could not download ffmpeg for styled export: {ex.Message}"));
        }
        finally
        {
            FfmpegAcquireGate.Release();
        }
    }

    private static string? ResolveFfmpegPath()
    {
        var configured = Environment.GetEnvironmentVariable("SCREENFAST_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var cached = Path.Combine(ScreenFastPaths.FfmpegFolderPath, CachedFfmpegRelativePath);
        if (File.Exists(cached))
        {
            return cached;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string> DownloadFfmpegAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ScreenFastPaths.FfmpegFolderPath);
        var tempZipPath = Path.Combine(ScreenFastPaths.FfmpegFolderPath, $"ffmpeg-{Guid.NewGuid():N}.zip");
        var tempExtractPath = Path.Combine(ScreenFastPaths.FfmpegFolderPath, $"extract-{Guid.NewGuid():N}");
        var finalRootPath = Path.Combine(ScreenFastPaths.FfmpegFolderPath, "ffmpeg-master-latest-win64-gpl");
        var finalFfmpegPath = Path.Combine(ScreenFastPaths.FfmpegFolderPath, CachedFfmpegRelativePath);

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(8);
            await using (var downloadStream = await httpClient.GetStreamAsync(FfmpegDownloadUrl, cancellationToken))
            await using (var fileStream = File.Create(tempZipPath))
            {
                await downloadStream.CopyToAsync(fileStream, cancellationToken);
            }

            ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);
            var extractedFfmpegPath = Directory
                .EnumerateFiles(tempExtractPath, "ffmpeg.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(extractedFfmpegPath) || !File.Exists(extractedFfmpegPath))
            {
                throw new InvalidOperationException("The ffmpeg archive did not contain bin\\ffmpeg.exe.");
            }

            if (Directory.Exists(finalRootPath))
            {
                Directory.Delete(finalRootPath, recursive: true);
            }

            var extractedRoot = Directory
                .EnumerateDirectories(tempExtractPath)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(extractedRoot))
            {
                throw new InvalidOperationException("The ffmpeg archive did not contain an extractable root folder.");
            }

            Directory.Move(extractedRoot, finalRootPath);
            if (!File.Exists(finalFfmpegPath))
            {
                throw new InvalidOperationException("ffmpeg was extracted, but the expected executable path was not created.");
            }

            return finalFfmpegPath;
        }
        finally
        {
            TryDelete(tempZipPath);
            TryDeleteDirectory(tempExtractPath);
        }
    }

    private static string EnsureOutputPath(string suggestedOutputPath, string inputVideoPath)
    {
        var outputPath = string.IsNullOrWhiteSpace(suggestedOutputPath)
            ? Path.Combine(
                Path.GetDirectoryName(inputVideoPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(inputVideoPath)}.styled.mp4")
            : suggestedOutputPath;
        var folder = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return outputPath;
    }

    private static string BuildFfmpegFailureMessage(StringBuilder stdErr)
    {
        var lines = stdErr
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tail = string.Join(" ", lines.TakeLast(4));
        return string.IsNullOrWhiteSpace(tail)
            ? "ffmpeg failed while rendering the styled export."
            : $"ffmpeg failed while rendering the styled export: {tail}";
    }

    private static string ToFfmpegColor(StyledExportColor color) =>
        $"0x{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static int MakeEven(int value)
    {
        var clamped = Math.Max(2, value);
        return clamped % 2 == 0 ? clamped : clamped - 1;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
