using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Aetherphone.Core;
using Aetherphone.Core.Reels;
using Aetherphone.Core.Video;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Aetherphone.Windows;

internal sealed class ReelSpikeWindow : Window, IDisposable
{
    private const string LogPrefix = "[ReelSpike] ";
    private const int DrawSampleCapacity = 4096;
    private const double ReportIntervalSeconds = 5d;
    private const float PortraitAspect = 9f / 16f;
    private const float Megabyte = 1024f * 1024f;
    private const int TranscodeWaitMilliseconds = 3000;
    private const int TeardownWaitMilliseconds = 3000;
    private const uint PanelColor = 0xFF000000;
    private const uint RailColor = 0x55FFFFFF;
    private const uint ProgressColor = 0xFFFFFFFF;

    private static readonly bool JitOptimized =
        typeof(ReelSpikeWindow).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    private readonly VideoEngine mogCast;
    private readonly float[] drawMilliseconds = new float[DrawSampleCapacity];
    private readonly float[] sortedMilliseconds = new float[DrawSampleCapacity];
    private readonly long[] loggedFirstFrames = new long[ReelPlayer.Depth];
    private readonly long[] loggedMotion = new long[ReelPlayer.Depth];
    private readonly string?[] loggedFailures = new string?[ReelPlayer.Depth];
    private readonly ReelSource[] window = new ReelSource[ReelPlayer.Depth];

    private ReelPlayer? player;
    private ReelSource[] queue = [];
    private int position;
    private volatile ReelSource[]? pendingQueue;
    private volatile bool stopRequested;
    private volatile bool nextRequested;
    private int sampleCount;
    private long allocatedTotal;
    private long allocatedMax;
    private int framesWithAllocation;
    private long reportStartedAt;
    private int reportReelFrames;
    private int reportMogCastFrames;
    private int reportSwipes;
    private string status = "Idle. Use reelspike play or transcode.";
    private int transcoding;
    private Task transcodeTask = Task.CompletedTask;
    private Task teardownTask = Task.CompletedTask;
    private CancellationTokenSource transcodeCancel = new();

    public ReelSpikeWindow(VideoEngine mogCast)
        : base("Aetherphone: Reel Spike###AetherphoneReelSpike")
    {
        this.mogCast = mogCast;
        Size = new Vector2(380, 780);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    internal void Run(string arguments)
    {
        var tokens = Tokenize(arguments);
        var verb = tokens.Count > 0 ? tokens[0].ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "transcode" when tokens.Count >= 2:
                StartTranscode(tokens);
                break;
            case "play" when tokens.Count >= 2:
                IsOpen = true;
                _ = PrepareQueueAsync(tokens.GetRange(1, Math.Min(tokens.Count - 1, ReelPlayer.Depth)).ToArray());
                break;
            case "next":
                nextRequested = true;
                break;
            case "stop":
                stopRequested = true;
                break;
            case "cancel":
                transcodeCancel.Cancel();
                break;
            default:
                AepLog.Info(LogPrefix
                    + "usage: reelspike transcode \"<path>\" [fill[=0..1]] [soft] [start=<s>] [end=<s>] | play \"<path>\" [\"<path>\" ...] | next | stop | cancel");
                break;
        }
    }

    public override void Draw()
    {
        ApplyRequests();
        if (player is not { } reels)
        {
            DrawStatus();
            return;
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        reels.Update();
        DrawReel(reels);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        RecordFrame((float)elapsed, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        DrawControls(reels);
        ObserveSlots(reels);
        ReportIfDue(reels);
        DrawStatus();
    }

    public override void OnClose()
    {
        ClosePlayer("window closed");
    }

    public void Dispose()
    {
        transcodeCancel.Cancel();
        _ = transcodeTask.Wait(TranscodeWaitMilliseconds);
        transcodeCancel.Dispose();
        _ = teardownTask.Wait(TeardownWaitMilliseconds);
        if (player is { } open)
        {
            player = null;
            open.Dispose();
        }
    }

    private void ApplyRequests()
    {
        if (stopRequested)
        {
            stopRequested = false;
            ClosePlayer("stop command");
        }

        if (pendingQueue is { } adopted)
        {
            pendingQueue = null;
            StartPlayer(adopted);
        }

        if (nextRequested)
        {
            nextRequested = false;
            Advance();
        }
    }

    private void StartPlayer(ReelSource[] sources)
    {
        ClosePlayer("new play command");
        if (mogCast.Dependencies.VideoLibraryPath is null)
        {
            status = "libmpv is not installed. Open MogCast once so it downloads its components.";
            AepLog.Warning(LogPrefix + "play: libmpv-2.dll is not installed; open MogCast once so it downloads it.");
            return;
        }

        if (DxHandler.Device is null)
        {
            status = "No Direct3D device (not running in game).";
            AepLog.Warning(LogPrefix + "play: DxHandler has no Direct3D device.");
            return;
        }

        queue = sources;
        position = 0;
        var volume = (int)MathF.Round(Plugin.Cfg.VideoVolume * 100f);
        player = new ReelPlayer(volume, Plugin.Cfg.VideoHardwareDecoding);
        Array.Clear(loggedFirstFrames);
        Array.Clear(loggedMotion);
        Array.Clear(loggedFailures);
        ResetReport();
        AepLog.Info(LogPrefix
            + $"play: {sources.Length} file(s), volume {volume}, hardware decoding {Plugin.Cfg.VideoHardwareDecoding}, MogCast active {mogCast.IsActive}, jit optimized {JitOptimized}");
        ShowWindow(player);
    }

    private void Advance()
    {
        if (player is not { } reels || queue.Length == 0)
        {
            return;
        }

        position++;
        reportSwipes++;
        AepLog.Info(LogPrefix + $"swipe to item {position}");
        ShowWindow(reels);
    }

    private void ShowWindow(ReelPlayer reels)
    {
        for (var offset = 0; offset < window.Length; offset++)
        {
            var item = position + offset;
            var source = queue[item % queue.Length];
            window[offset] = source with { Id = item.ToString(CultureInfo.InvariantCulture) };
        }

        reels.Show(window);
    }

    private void ClosePlayer(string reason)
    {
        if (player is not { } closing)
        {
            return;
        }

        player = null;
        var started = Stopwatch.GetTimestamp();
        var teardown = closing.CloseAsync();
        AepLog.Info(LogPrefix
            + $"player close ({reason}) returned in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2} ms on the calling thread; mpv teardown continues on workers");
        teardownTask = LogTeardownAsync(teardown, started);
        status = "Idle.";
    }

    private static async Task LogTeardownAsync(Task teardown, long started)
    {
        try
        {
            await teardown.ConfigureAwait(false);
            AepLog.Info(LogPrefix
                + $"background teardown finished {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms after close (textures released on the render thread)");
        }
        catch (Exception exception)
        {
            AepLog.Error(exception, LogPrefix + "background teardown failed");
        }
    }

    private void DrawReel(ReelPlayer reels)
    {
        var scale = UiScale.Current;
        var available = ImGui.GetContentRegionAvail();
        var reserved = (Metrics.Size.FieldHeight * 2f + Metrics.Space.Xxl * 3f) * scale;
        var height = MathF.Max(Metrics.Size.Row * scale, MathF.Min(available.Y - reserved, available.X / PortraitAspect));
        var width = height * PortraitAspect;
        var origin = ImGui.GetCursorScreenPos();
        var panelMin = new Vector2(origin.X + (available.X - width) * 0.5f, origin.Y);
        var panelMax = panelMin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(panelMin, panelMax, PanelColor);

        var frame = reels.CurrentFrame;
        if (frame.HasFrame)
        {
            var frameAspect = (float)frame.Width / frame.Height;
            var fitted = frameAspect > PortraitAspect
                ? new Vector2(width, width / frameAspect)
                : new Vector2(height * frameAspect, height);
            var imageMin = panelMin + (new Vector2(width, height) - fitted) * 0.5f;
            drawList.AddImage(new ImTextureID(frame.TextureHandle), imageMin, imageMin + fitted);
        }

        var progress = reels.CurrentProgress;
        var fraction = progress.DurationSeconds > 0d
            ? Math.Clamp((float)(progress.PositionSeconds / progress.DurationSeconds), 0f, 1f)
            : 0f;
        var barTop = panelMax.Y - Metrics.Stroke.Ring * scale;
        drawList.AddRectFilled(new Vector2(panelMin.X, barTop), panelMax, RailColor);
        drawList.AddRectFilled(new Vector2(panelMin.X, barTop), new Vector2(panelMin.X + width * fraction, panelMax.Y),
            ProgressColor);
        ImGui.Dummy(new Vector2(available.X, height));
    }

    private void DrawControls(ReelPlayer reels)
    {
        var gap = Metrics.Space.Sm * UiScale.Current;
        if (ImGui.Button(reels.Paused ? "Play" : "Pause"))
        {
            reels.SetPaused(!reels.Paused);
        }

        ImGui.SameLine(0f, gap);
        if (ImGui.Button(reels.Muted ? "Unmute" : "Mute"))
        {
            reels.SetMuted(!reels.Muted);
        }

        ImGui.SameLine(0f, gap);
        if (ImGui.Button("Next"))
        {
            Advance();
        }

        ImGui.SameLine(0f, gap);
        if (ImGui.Button("Stop"))
        {
            stopRequested = true;
        }
    }

    private void DrawStatus()
    {
        var style = TextStyles.Footnote;
        using (Plugin.Fonts.Push(style.Scale, style.Weight))
        {
            Typography.Wrapped(status);
        }
    }

    private void RecordFrame(float milliseconds, long allocated)
    {
        if (sampleCount < DrawSampleCapacity)
        {
            drawMilliseconds[sampleCount] = milliseconds;
            sampleCount++;
        }

        allocatedTotal += allocated;
        allocatedMax = Math.Max(allocatedMax, allocated);
        if (allocated > 0)
        {
            framesWithAllocation++;
        }
    }

    private void ObserveSlots(ReelPlayer reels)
    {
        for (var slotIndex = 0; slotIndex < ReelPlayer.Depth; slotIndex++)
        {
            var slot = reels.SlotStatus(slotIndex);
            if (slot.FirstFrameTimestamp != 0 && slot.FirstFrameTimestamp != loggedFirstFrames[slotIndex])
            {
                loggedFirstFrames[slotIndex] = slot.FirstFrameTimestamp;
                var waited = Stopwatch.GetElapsedTime(slot.LoadStartedTimestamp, slot.FirstFrameTimestamp);
                AepLog.Info(LogPrefix
                    + $"slot {slotIndex} item {slot.Id} first frame {waited.TotalMilliseconds:F0} ms after load ({(slot.Current ? "current" : "preloaded")})");
            }

            if (slot.MotionTimestamp != 0 && slot.MotionTimestamp != loggedMotion[slotIndex])
            {
                loggedMotion[slotIndex] = slot.MotionTimestamp;
                var waited = Stopwatch.GetElapsedTime(slot.ActivatedTimestamp, slot.MotionTimestamp);
                var preloaded = slot.FirstFrameTimestamp != 0 && slot.FirstFrameTimestamp < slot.ActivatedTimestamp;
                AepLog.Info(LogPrefix
                    + $"item {slot.Id} playing {waited.TotalMilliseconds:F0} ms after it became current (first frame was {(preloaded ? "already on screen" : "not ready yet")})");
            }

            if (slot.Failure is { } failure && !string.Equals(failure, loggedFailures[slotIndex], StringComparison.Ordinal))
            {
                loggedFailures[slotIndex] = failure;
                AepLog.Warning(LogPrefix + $"slot {slotIndex} item {slot.Id} failed: {failure}");
            }
        }
    }

    private void ResetReport()
    {
        sampleCount = 0;
        allocatedTotal = 0;
        allocatedMax = 0;
        framesWithAllocation = 0;
        reportStartedAt = Stopwatch.GetTimestamp();
        reportReelFrames = player?.CurrentFrameVersion ?? 0;
        reportMogCastFrames = mogCast.FrameVersion;
        reportSwipes = 0;
    }

    private void ReportIfDue(ReelPlayer reels)
    {
        var seconds = Stopwatch.GetElapsedTime(reportStartedAt).TotalSeconds;
        if (seconds < ReportIntervalSeconds || sampleCount == 0)
        {
            return;
        }

        Array.Copy(drawMilliseconds, sortedMilliseconds, sampleCount);
        Array.Sort(sortedMilliseconds, 0, sampleCount);
        var total = 0d;
        for (var index = 0; index < sampleCount; index++)
        {
            total += sortedMilliseconds[index];
        }

        var average = total / sampleCount;
        var percentile95 = sortedMilliseconds[Math.Min(sampleCount - 1, (int)(sampleCount * 0.95f))];
        var maximum = sortedMilliseconds[sampleCount - 1];
        var reelFps = (reels.CurrentFrameVersion - reportReelFrames) / seconds;
        var mogCastFps = (mogCast.FrameVersion - reportMogCastFrames) / seconds;
        var progress = reels.CurrentProgress;
        var line =
            $"draw path (Update + quad + bar) over {sampleCount} frames: avg {average:F3} ms, p95 {percentile95:F3} ms, max {maximum:F3} ms | managed alloc avg {allocatedTotal / (double)sampleCount:F1} B/frame, max {allocatedMax} B, frames allocating {framesWithAllocation} | reel {reelFps:F1} fps at {progress.PositionSeconds:F1}/{progress.DurationSeconds:F1} s, swipes {reportSwipes} | MogCast {(mogCast.IsActive ? "active" : "idle")} {mogCastFps:F1} fps";
        AepLog.Info(LogPrefix + line);
        status = line;
        ResetReport();
    }

    private async Task PrepareQueueAsync(string[] paths)
    {
        var sources = new ReelSource[paths.Length];
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            var width = 0;
            var height = 0;
            try
            {
                var info = await Task.Run(() => ReelMediaProbe.Read(path)).ConfigureAwait(false);
                width = info.DisplayWidth;
                height = info.DisplayHeight;
                AepLog.Info(LogPrefix + $"play: {path} probes as {Describe(info)}");
            }
            catch (ReelTranscodeException exception)
            {
                AepLog.Warning(LogPrefix
                    + $"play: probing {path} failed ({exception.Failure}: {exception.Message}); using a 720x1280 texture");
            }

            sources[index] = new ReelSource(string.Empty, path, width, height);
        }

        pendingQueue = sources;
    }

    private void StartTranscode(List<string> tokens)
    {
        if (Interlocked.CompareExchange(ref transcoding, 1, 0) != 0)
        {
            AepLog.Warning(LogPrefix + "a transcode is already running; use reelspike cancel first.");
            return;
        }

        var path = tokens[1];
        var fill = false;
        var fillPosition = 0.5f;
        var allowHardware = true;
        var start = 0d;
        var end = double.NaN;
        for (var index = 2; index < tokens.Count; index++)
        {
            var token = tokens[index].ToLowerInvariant();
            if (token == "fill")
            {
                fill = true;
            }
            else if (token.StartsWith("fill=", StringComparison.Ordinal))
            {
                fill = true;
                fillPosition = (float)ParseNumber(token[5..], 0.5d);
            }
            else if (token == "soft")
            {
                allowHardware = false;
            }
            else if (token.StartsWith("start=", StringComparison.Ordinal))
            {
                start = ParseNumber(token[6..], 0d);
            }
            else if (token.StartsWith("end=", StringComparison.Ordinal))
            {
                end = ParseNumber(token[4..], double.NaN);
            }
        }

        transcodeCancel.Dispose();
        transcodeCancel = new CancellationTokenSource();
        transcodeTask = TranscodeAsync(path, fill, fillPosition, allowHardware, start, end, transcodeCancel.Token);
    }

    private async Task TranscodeAsync(string path, bool fill, float fillPosition, bool allowHardware, double start,
        double end, CancellationToken token)
    {
        try
        {
            AepLog.Info(LogPrefix
                + $"transcode start: {path} mode={(fill ? $"fill 9:16 at {fillPosition:F2}" : "original")} encoder={(allowHardware ? "hardware allowed" : "software only")} jit optimized={JitOptimized} wine={WineEnvironment.IsWine} os={Environment.OSVersion.VersionString} cores={Environment.ProcessorCount}");
            ReelMediaInfo input;
            try
            {
                input = await Task.Run(() => ReelMediaProbe.Read(path), token).ConfigureAwait(false);
            }
            catch (ReelTranscodeException exception)
            {
                AepLog.Warning(LogPrefix + $"transcode FAILED at probe ({exception.Failure}): {exception.Message}");
                return;
            }

            AepLog.Info(LogPrefix + "input: " + Describe(input));
            var trimEnd = double.IsNaN(end)
                ? Math.Min(input.DurationSeconds, start + ReelFormat.MaxDurationSeconds)
                : end;
            ReelCrop? crop = fill ? ReelCrop.FillPortrait(input.DisplayWidth, input.DisplayHeight, fillPosition) : null;
            var folder = Path.Combine(Path.GetTempPath(), "AetherphoneReelSpike");
            Directory.CreateDirectory(folder);
            var output = Path.Combine(folder,
                $"{Path.GetFileNameWithoutExtension(path)}-{(fill ? "fill" : "original")}-{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.mp4");
            using var monitor = new TranscodeMonitor();
            var request = new ReelTranscodeRequest(path, output, start, trimEnd, crop, allowHardware);
            var result = await ReelTranscoder.TranscodeAsync(request, monitor, token).ConfigureAwait(false);
            LogResult(result, monitor, output);
            if (!result.Succeeded)
            {
                return;
            }

            try
            {
                var written = await Task.Run(() => ReelMediaProbe.Read(output), token).ConfigureAwait(false);
                AepLog.Info(LogPrefix + "output probe: " + Describe(written));
            }
            catch (ReelTranscodeException exception)
            {
                AepLog.Warning(LogPrefix + $"output probe failed ({exception.Failure}): {exception.Message}");
            }

            AepLog.Info(LogPrefix + $"to watch it in the reel player: reelspike play \"{output}\"");
        }
        catch (OperationCanceledException)
        {
            AepLog.Info(LogPrefix + "transcode cancelled.");
        }
        catch (Exception exception)
        {
            AepLog.Error(exception, LogPrefix + "transcode crashed");
        }
        finally
        {
            Interlocked.Exchange(ref transcoding, 0);
        }
    }

    private static void LogResult(ReelTranscodeResult result, TranscodeMonitor monitor, string output)
    {
        var report = result.Report;
        AepLog.Info(LogPrefix
            + $"media foundation: H.264 encoders [{report.VideoEncoders}] | AAC encoders [{report.AudioEncoders}]");
        AepLog.Info(LogPrefix
            + $"pipeline: decode [{report.DecoderChain}] | encode [{report.EncoderChain}] | writer attempts [{report.WriterAttempts}] | rotation [{report.Rotation}]");
        AepLog.Info(LogPrefix
            + $"memory: private before {monitor.BaselinePrivateBytes / Megabyte:F1} MB, peak {monitor.PeakPrivateBytes / Megabyte:F1} MB (+{(monitor.PeakPrivateBytes - monitor.BaselinePrivateBytes) / Megabyte:F1} MB, {monitor.Samples} samples) | transcode thread managed allocations {report.ManagedBytesAllocated / Megabyte:F2} MB");
        if (!result.Succeeded)
        {
            AepLog.Warning(LogPrefix
                + $"transcode FAILED ({result.Failure}) after {report.ElapsedSeconds:F2} s: {result.Detail} | frames decoded {report.FramesDecoded}, written {report.FramesWritten}");
            return;
        }

        var factor = report.ElapsedSeconds > 0d ? report.TrimSeconds / report.ElapsedSeconds : 0d;
        var layout = report.Layout;
        AepLog.Info(LogPrefix
            + $"transcode ok: {report.TrimSeconds:F2} s of video in {report.ElapsedSeconds:F2} s wall, real-time factor {factor:F2}x (video seconds per wall second)");
        AepLog.Info(LogPrefix
            + $"time split: read+decode {report.ReadSeconds:F2} s, crop+scale {report.ScaleSeconds:F2} s ({(report.FramesWritten > 0 ? report.ScaleSeconds * 1000d / report.FramesWritten : 0d):F2} ms/frame; copy path {report.CopiedFrames} frames, sampled path {report.SampledFrames} frames, {report.ScaleWorkers} workers), write+encode {report.WriteSeconds:F2} s, finalize {report.FinalizeSeconds:F2} s");
        AepLog.Info(LogPrefix
            + $"frames: decoded {report.FramesDecoded}, written {report.FramesWritten} at {report.Cadence.Numerator}/{report.Cadence.Denominator} | output {layout.Width}x{layout.Height}, content {layout.Content.X},{layout.Content.Y} {layout.Content.Width}x{layout.Content.Height}, source rect {layout.SourceX:F1},{layout.SourceY:F1} {layout.SourceWidth:F1}x{layout.SourceHeight:F1} | audio {(report.HasAudio ? $"AAC, {report.AudioBytesWritten / Megabyte:F2} MB PCM in" : "none")}{(report.AudioNote is { } note ? $" ({note})" : string.Empty)}");
        var megabits = report.TrimSeconds > 0d ? report.OutputBytes * 8d / report.TrimSeconds / 1_000_000d : 0d;
        AepLog.Info(LogPrefix
            + $"output: {output} | {report.OutputBytes / Megabyte:F2} MB ({megabits:F2} Mbps overall) | sink boxes [{report.SinkLayout.BoxOrder}] final boxes [{report.Output.BoxOrder}] | faststart {(report.Output.MoovBeforeMdat ? "yes" : "no")} (sink wrote moov first: {(report.SinkLayout.MoovBeforeMdat ? "yes" : "no")}, rewritten: {(report.FastStartRewritten ? "yes" : "no")}) | mvhd duration {report.Output.DurationSeconds:F3} s");
    }

    private static string Describe(in ReelMediaInfo info)
    {
        var text = new StringBuilder();
        text.Append("container ").Append(info.Container)
            .Append(", ").Append((info.FileBytes / Megabyte).ToString("F2", CultureInfo.InvariantCulture)).Append(" MB")
            .Append(", duration ").Append(info.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(" s")
            .Append(", video ").Append(info.VideoCodec)
            .Append(" coded ").Append(info.CodedWidth).Append('x').Append(info.CodedHeight)
            .Append(" display ").Append(info.DisplayWidth).Append('x').Append(info.DisplayHeight)
            .Append(" rotation attribute ").Append(info.RotationAttribute)
            .Append(" fps ").Append(info.FrameRateNumerator).Append('/').Append(info.FrameRateDenominator)
            .Append(" (").Append(info.FrameRate.ToString("F3", CultureInfo.InvariantCulture)).Append(')');
        if (info.AudioCodec is { } audio)
        {
            text.Append(", audio ").Append(audio).Append(' ').Append(info.AudioSampleRate).Append(" Hz ")
                .Append(info.AudioChannels).Append(" ch");
        }
        else
        {
            text.Append(", no audio");
        }

        return text.ToString();
    }

    private static double ParseNumber(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private sealed class TranscodeMonitor : IProgress<float>, IDisposable
    {
        private readonly Process process = Process.GetCurrentProcess();
        private int lastDecile = -1;

        internal TranscodeMonitor()
        {
            BaselinePrivateBytes = process.PrivateMemorySize64;
            PeakPrivateBytes = BaselinePrivateBytes;
        }

        internal long BaselinePrivateBytes { get; }

        internal long PeakPrivateBytes { get; private set; }

        internal int Samples { get; private set; }

        public void Report(float value)
        {
            process.Refresh();
            var privateBytes = process.PrivateMemorySize64;
            PeakPrivateBytes = Math.Max(PeakPrivateBytes, privateBytes);
            Samples++;
            var decile = (int)(value * 10f);
            if (decile <= lastDecile)
            {
                return;
            }

            lastDecile = decile;
            AepLog.Info(LogPrefix + $"progress {decile * 10}% (private {privateBytes / Megabyte:F1} MB)");
        }

        public void Dispose()
        {
            process.Dispose();
        }
    }
}
