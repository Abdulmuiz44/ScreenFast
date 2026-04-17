using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class WindowsCursorTelemetryCaptureService : IRecordingTelemetryCaptureService
{
    private const int DefaultSampleRateHz = 20;

    private readonly IScreenFastLogService _logService;

    public WindowsCursorTelemetryCaptureService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public OperationResult<IRecordingTelemetrySession> Start(RecordingTelemetryStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return OperationResult<IRecordingTelemetrySession>.Failure(AppError.InvalidState("Telemetry cannot start without a recording session id."));
        }

        try
        {
            var warnings = new List<string>();
            var bounds = TryResolveSourceBounds(request.Source, warnings);
            var sampleRate = request.SampleRateHz > 0 ? request.SampleRateHz : DefaultSampleRateHz;
            var session = new CursorTelemetrySession(request.SessionId, sampleRate, request.StartedAtUtc, bounds, warnings, _logService);
            _logService.Info(
                "telemetry.started",
                "ScreenFast started cursor telemetry capture.",
                new Dictionary<string, object?>
                {
                    ["sessionId"] = request.SessionId,
                    ["sampleRateHz"] = sampleRate,
                    ["sourceSummary"] = $"{request.Source.TypeDisplayName}: {request.Source.DisplayName}",
                    ["sourceBoundsResolved"] = bounds is not null
                });
            return OperationResult<IRecordingTelemetrySession>.Success(session);
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "telemetry.start_failed",
                "ScreenFast could not start cursor telemetry. Recording can continue without it.",
                new Dictionary<string, object?> { ["error"] = ex.Message });
            return OperationResult<IRecordingTelemetrySession>.Failure(
                AppError.RecordingFailed($"Cursor telemetry could not start: {ex.Message}"));
        }
    }

    private static SourceBoundsSnapshot? TryResolveSourceBounds(CaptureSourceModel source, List<string> warnings)
    {
        if (!TryParseHandle(source.SourceId, out var kind, out var handle) || handle == nint.Zero)
        {
            warnings.Add("Source bounds could not be resolved because the source id was not a supported window or display handle.");
            return null;
        }

        if (kind == "window")
        {
            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                warnings.Add("Window bounds could not be resolved for cursor source-relative coordinates.");
                return null;
            }

            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            if (width == 0 || height == 0)
            {
                warnings.Add("Window bounds resolved to an empty rectangle.");
                return null;
            }

            return new SourceBoundsSnapshot(rect.Left, rect.Top, width, height);
        }

        if (kind == "display")
        {
            var monitorInfo = new NativeMethods.MonitorInfoEx
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                DeviceName = string.Empty
            };

            if (!NativeMethods.GetMonitorInfo(handle, ref monitorInfo))
            {
                warnings.Add("Display bounds could not be resolved for cursor source-relative coordinates.");
                return null;
            }

            var width = Math.Max(0, monitorInfo.Monitor.Right - monitorInfo.Monitor.Left);
            var height = Math.Max(0, monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
            if (width == 0 || height == 0)
            {
                warnings.Add("Display bounds resolved to an empty rectangle.");
                return null;
            }

            return new SourceBoundsSnapshot(monitorInfo.Monitor.Left, monitorInfo.Monitor.Top, width, height);
        }

        warnings.Add("Source bounds could not be resolved because the source kind was not supported.");
        return null;
    }

    private static bool TryParseHandle(string sourceId, out string kind, out nint handle)
    {
        kind = string.Empty;
        handle = nint.Zero;

        var parts = sourceId.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        kind = parts[0].ToLowerInvariant();
        var rawValue = parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[1][2..] : parts[1];
        if (!long.TryParse(rawValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        handle = new nint(parsed);
        return true;
    }

    private sealed class CursorTelemetrySession : IRecordingTelemetrySession
    {
        private readonly object _sync = new();
        private readonly List<CursorPositionSample> _samples = [];
        private readonly List<CursorClickEvent> _clickEvents = [];
        private readonly List<string> _warnings;
        private readonly SourceBoundsSnapshot? _sourceBounds;
        private readonly IScreenFastLogService _logService;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _samplingTask;
        private readonly long _startedTimestamp;
        private long _runningStartedTimestamp;
        private TimeSpan _elapsedBeforeCurrentRun = TimeSpan.Zero;
        private bool _isPaused;
        private bool _isStopped;
        private bool _leftDown;
        private bool _rightDown;
        private bool _middleDown;
        private DateTimeOffset? _stoppedAtUtc;

        public CursorTelemetrySession(
            string sessionId,
            int sampleRateHz,
            DateTimeOffset startedAtUtc,
            SourceBoundsSnapshot? sourceBounds,
            IReadOnlyList<string> warnings,
            IScreenFastLogService logService)
        {
            SessionId = sessionId;
            SampleRateHz = sampleRateHz;
            StartedAtUtc = startedAtUtc;
            _sourceBounds = sourceBounds;
            _warnings = warnings.ToList();
            _logService = logService;
            _startedTimestamp = Stopwatch.GetTimestamp();
            _runningStartedTimestamp = _startedTimestamp;
            _samplingTask = Task.Run(RunAsync);
        }

        public string SessionId { get; }

        private int SampleRateHz { get; }

        private DateTimeOffset StartedAtUtc { get; }

        public void Pause(TimeSpan elapsed)
        {
            lock (_sync)
            {
                if (_isStopped)
                {
                    return;
                }

                _elapsedBeforeCurrentRun = elapsed;
                _isPaused = true;
            }
        }

        public void Resume(TimeSpan elapsed)
        {
            lock (_sync)
            {
                if (_isStopped)
                {
                    return;
                }

                _elapsedBeforeCurrentRun = elapsed;
                _runningStartedTimestamp = Stopwatch.GetTimestamp();
                _isPaused = false;
            }
        }

        public async Task<RecordingTelemetryTimeline> StopAsync(TimeSpan finalDuration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_isStopped)
                {
                    return BuildTimelineUnsafe();
                }

                _elapsedBeforeCurrentRun = finalDuration;
                _stoppedAtUtc = DateTimeOffset.UtcNow;
                _isStopped = true;
            }

            _shutdown.Cancel();
            try
            {
                await _samplingTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _warnings.Add($"Telemetry sampling did not stop cleanly: {ex.Message}");
                }
            }

            lock (_sync)
            {
                var timeline = BuildTimelineUnsafe();
                _logService.Info(
                    "telemetry.stopped",
                    "ScreenFast stopped cursor telemetry capture.",
                    new Dictionary<string, object?>
                    {
                        ["sessionId"] = SessionId,
                        ["cursorSampleCount"] = timeline.CursorSamples.Count,
                        ["clickEventCount"] = timeline.ClickEvents.Count,
                        ["warningCount"] = timeline.Warnings.Count
                    });
                return timeline;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync(_elapsedBeforeCurrentRun);
            }
            catch
            {
            }
            finally
            {
                _shutdown.Dispose();
            }
        }

        private async Task RunAsync()
        {
            var period = TimeSpan.FromMilliseconds(Math.Max(10, 1000d / SampleRateHz));
            try
            {
                using var timer = new PeriodicTimer(period);
                while (await timer.WaitForNextTickAsync(_shutdown.Token))
                {
                    CaptureTick();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _warnings.Add($"Telemetry sampling stopped unexpectedly: {ex.Message}");
                }
                _logService.Warning(
                    "telemetry.sampling_failed",
                    "Cursor telemetry sampling stopped unexpectedly. Recording can continue without more telemetry.",
                    new Dictionary<string, object?> { ["sessionId"] = SessionId, ["error"] = ex.Message });
            }
        }

        private void CaptureTick()
        {
            lock (_sync)
            {
                if (_isStopped || _isPaused)
                {
                    return;
                }
            }

            if (!NativeMethods.GetCursorPos(out var point))
            {
                lock (_sync)
                {
                    if (!_warnings.Contains("GetCursorPos failed; cursor samples may be incomplete."))
                    {
                        _warnings.Add("GetCursorPos failed; cursor samples may be incomplete.");
                    }
                }
                return;
            }

            var offset = GetCurrentOffset();
            var projection = ProjectToSource(point.X, point.Y);
            var sample = new CursorPositionSample(offset, point.X, point.Y, projection.SourceX, projection.SourceY, projection.IsInsideSource);
            var leftDown = IsButtonDown(NativeMethods.VirtualKeyLeftButton);
            var rightDown = IsButtonDown(NativeMethods.VirtualKeyRightButton);
            var middleDown = IsButtonDown(NativeMethods.VirtualKeyMiddleButton);

            lock (_sync)
            {
                if (_isStopped || _isPaused)
                {
                    return;
                }

                _samples.Add(sample);
                AddClickTransition(CursorClickButton.Left, _leftDown, leftDown, sample);
                AddClickTransition(CursorClickButton.Right, _rightDown, rightDown, sample);
                AddClickTransition(CursorClickButton.Middle, _middleDown, middleDown, sample);
                _leftDown = leftDown;
                _rightDown = rightDown;
                _middleDown = middleDown;
            }
        }

        private long GetCurrentOffset()
        {
            lock (_sync)
            {
                var elapsed = _isPaused
                    ? _elapsedBeforeCurrentRun
                    : _elapsedBeforeCurrentRun + Stopwatch.GetElapsedTime(_runningStartedTimestamp, Stopwatch.GetTimestamp());
                return Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
            }
        }

        private (int? SourceX, int? SourceY, bool IsInsideSource) ProjectToSource(int screenX, int screenY)
        {
            if (_sourceBounds is null)
            {
                return (null, null, false);
            }

            var sourceX = screenX - _sourceBounds.Left;
            var sourceY = screenY - _sourceBounds.Top;
            var inside = sourceX >= 0 && sourceY >= 0 && sourceX < _sourceBounds.Width && sourceY < _sourceBounds.Height;
            return (sourceX, sourceY, inside);
        }

        private void AddClickTransition(CursorClickButton button, bool wasDown, bool isDown, CursorPositionSample sample)
        {
            if (wasDown == isDown)
            {
                return;
            }

            _clickEvents.Add(
                new CursorClickEvent(
                    sample.OffsetMilliseconds,
                    button,
                    isDown ? CursorClickEventKind.Down : CursorClickEventKind.Up,
                    sample.ScreenX,
                    sample.ScreenY,
                    sample.SourceX,
                    sample.SourceY,
                    sample.IsInsideSource));
        }

        private RecordingTelemetryTimeline BuildTimelineUnsafe()
        {
            return new RecordingTelemetryTimeline(
                SampleRateHz,
                StartedAtUtc,
                _stoppedAtUtc,
                _sourceBounds,
                _samples.ToArray(),
                _clickEvents.ToArray(),
                _warnings.Distinct(StringComparer.Ordinal).ToArray());
        }

        private static bool IsButtonDown(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
    }

    private static class NativeMethods
    {
        public const int VirtualKeyLeftButton = 0x01;
        public const int VirtualKeyRightButton = 0x02;
        public const int VirtualKeyMiddleButton = 0x04;

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(nint windowHandle, out Rect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfoEx monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }
    }
}
