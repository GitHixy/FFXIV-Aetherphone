using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Reels;

internal enum ReelShareStage : byte
{
    Converting,
    Uploading,
    Posting,
    Done,
    Failed,
    Cancelled,
}

internal sealed record ReelShareRequest(
    string SourcePath,
    double StartSeconds,
    double EndSeconds,
    double CoverSeconds,
    ReelCrop? Crop,
    string Caption,
    PhotoTagInput[]? PhotoTags,
    string[]? CollaboratorIds,
    string? AltText,
    bool Sensitive);

internal sealed record ReelShareState(ReelShareStage Stage, ReelShareFailure Failure, PostDto? Created)
{
    internal bool Busy => Stage is ReelShareStage.Converting or ReelShareStage.Uploading or ReelShareStage.Posting;

    internal bool Cancellable => Stage is ReelShareStage.Converting or ReelShareStage.Uploading;
}

// Converts, makes the cover, uploads both and creates the reel, in that order and in the background. The uploads
// happen only after the local work succeeded and right before the create, so the server never holds an upload that
// no reel will claim for longer than one request.
internal sealed class ReelShareJob : IDisposable
{
    public const string VideoScope = "reel";
    public const string PosterScope = "reel-poster";
    public const string VideoContentType = "video/mp4";
    public const string PosterContentType = "image/jpeg";
    public const long MaxVideoBytes = 60L * 1024 * 1024;

    // Far inside the server's 48 hour orphan window, so a retried create never names an upload the purge took.
    private static readonly TimeSpan UploadReuseWindow = TimeSpan.FromHours(1);

    private readonly IReelMedia media;
    private readonly IReelPublisher publisher;
    private readonly string workFolder;
    private readonly bool allowHardwareEncoder;
    private readonly ProgressSink progressSink;
    private readonly Action<AepFailure> recordFailure;
    private readonly object gate = new();
    private CancellationTokenSource cancellation = new();
    private volatile ReelShareState state = new(ReelShareStage.Converting, default, null);
    private string? convertedPath;
    private byte[]? poster;
    private string? videoKey;
    private string? posterKey;
    private DateTime uploadedAtUtc;
    private AepFailure lastFailure;
    private bool createFailedInTransit;
    private bool disposed;

    public ReelShareJob(IReelMedia media, IReelPublisher publisher, string workFolder, ReelShareRequest request,
        bool allowHardwareEncoder)
    {
        this.media = media;
        this.publisher = publisher;
        this.workFolder = workFolder;
        this.allowHardwareEncoder = allowHardwareEncoder;
        Request = request;
        progressSink = new ProgressSink();
        recordFailure = failure => lastFailure = failure;
    }

    public ReelShareRequest Request { get; }

    public ReelShareState State => state;

    public float Progress => progressSink.Value;

    public Task Running { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        lock (gate)
        {
            if (disposed || Running is { IsCompleted: false })
            {
                return;
            }

            state = new ReelShareState(convertedPath is null ? ReelShareStage.Converting : ReelShareStage.Uploading,
                default, null);
            var token = cancellation.Token;
            Running = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (!state.Cancellable)
            {
                return;
            }

            cancellation.Cancel();
        }
    }

    public bool Retry()
    {
        lock (gate)
        {
            if (disposed || state.Stage != ReelShareStage.Failed || !state.Failure.Retryable)
            {
                return false;
            }

            if (!state.Failure.KeepsUpload)
            {
                ForgetUpload();
            }

            cancellation.Dispose();
            cancellation = new CancellationTokenSource();
        }

        Start();
        return true;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation.Cancel();
        }

        _ = Running.ContinueWith(_ =>
        {
            DeleteConverted();
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            var outcome = await ShareAsync(token).ConfigureAwait(false);
            state = outcome;
            if (outcome.Stage is ReelShareStage.Done or ReelShareStage.Cancelled)
            {
                DeleteConverted();
            }
        }
        catch (OperationCanceledException)
        {
            state = new ReelShareState(ReelShareStage.Cancelled, default, null);
            DeleteConverted();
        }
        catch (Exception exception)
        {
            AepLog.Warning(exception, "[Reels] sharing a reel failed unexpectedly");
            state = Failed(ReelShareFailure.Converting(ReelTranscodeFailure.EncodeFailed));
        }
    }

    private async Task<ReelShareState> ShareAsync(CancellationToken token)
    {
        if (convertedPath is not null && !File.Exists(convertedPath))
        {
            convertedPath = null;
            ForgetUpload();
        }

        if (convertedPath is null)
        {
            var converted = await ConvertAsync(token).ConfigureAwait(false);
            if (converted is not null)
            {
                return converted;
            }
        }

        if (poster is null)
        {
            var covered = await CoverAsync(token).ConfigureAwait(false);
            if (covered is not null)
            {
                return covered;
            }
        }

        if (!UploadIsFresh())
        {
            var uploaded = await UploadAsync(token).ConfigureAwait(false);
            if (uploaded is not null)
            {
                return uploaded;
            }
        }

        token.ThrowIfCancellationRequested();
        Enter(ReelShareStage.Posting, 0f);
        lastFailure = default;
        var request = Request;
        var created = await publisher.CreateAsync(new CreateReelRequest(request.Caption.Trim(), videoKey!, posterKey!,
            request.PhotoTags, request.CollaboratorIds, string.IsNullOrWhiteSpace(request.AltText) ? null : request.AltText,
            request.Sensitive), CancellationToken.None, recordFailure).ConfigureAwait(false);
        if (created is not null)
        {
            ForgetUpload();
            return new ReelShareState(ReelShareStage.Done, default, created);
        }

        var failure = ReelShareFailure.Creating(lastFailure);
        if (createFailedInTransit && string.Equals(lastFailure.Code, ReelErrorCodes.MediaInUse, StringComparison.Ordinal))
        {
            // The earlier attempt reached the server after all: its reel already holds these uploads.
            ForgetUpload();
            return new ReelShareState(ReelShareStage.Done, default, null);
        }

        createFailedInTransit = failure.KeepsUpload;
        return Failed(failure);
    }

    private async Task<ReelShareState?> ConvertAsync(CancellationToken token)
    {
        Enter(ReelShareStage.Converting, 0f);
        Directory.CreateDirectory(workFolder);
        var output = Path.Combine(workFolder, $"reel-{Guid.NewGuid():N}.mp4");
        var request = Request;
        var result = await media.TranscodeAsync(new ReelTranscodeRequest(request.SourcePath, output,
            request.StartSeconds, request.EndSeconds, request.Crop, allowHardwareEncoder), progressSink, token)
            .ConfigureAwait(false);
        if (result.Failure == ReelTranscodeFailure.Cancelled)
        {
            return new ReelShareState(ReelShareStage.Cancelled, default, null);
        }

        if (!result.Succeeded)
        {
            return Failed(ReelShareFailure.Converting(result.Failure));
        }

        convertedPath = output;
        if (FileBytes(output) > MaxVideoBytes)
        {
            DeleteConverted();
            return Failed(ReelShareFailure.TooBig);
        }

        return null;
    }

    private async Task<ReelShareState?> CoverAsync(CancellationToken token)
    {
        var request = Request;
        var result = await media.PosterAsync(request.SourcePath, request.CoverSeconds, request.Crop, token)
            .ConfigureAwait(false);
        if (result.Failure == ReelTranscodeFailure.Cancelled)
        {
            return new ReelShareState(ReelShareStage.Cancelled, default, null);
        }

        if (!result.Succeeded)
        {
            return Failed(ReelShareFailure.Covering(result.Failure));
        }

        poster = result.Jpeg;
        return null;
    }

    private async Task<ReelShareState?> UploadAsync(CancellationToken token)
    {
        ForgetUpload();
        Enter(ReelShareStage.Uploading, 0f);
        lastFailure = default;
        var videoSlot = await publisher.ReserveAsync(VideoContentType, VideoScope, token, recordFailure)
            .ConfigureAwait(false);
        if (videoSlot is null)
        {
            return Failed(ReelShareFailure.Uploading(lastFailure));
        }

        if (!await publisher.UploadFileAsync(videoSlot.UploadUrl, convertedPath!, VideoContentType, progressSink, token,
                recordFailure).ConfigureAwait(false))
        {
            return Failed(ReelShareFailure.Uploading(lastFailure));
        }

        var posterSlot = await publisher.ReserveAsync(PosterContentType, PosterScope, token, recordFailure)
            .ConfigureAwait(false);
        if (posterSlot is null)
        {
            return Failed(ReelShareFailure.Uploading(lastFailure));
        }

        if (!await publisher.UploadBytesAsync(posterSlot.UploadUrl, poster!, PosterContentType, token, recordFailure)
                .ConfigureAwait(false))
        {
            return Failed(ReelShareFailure.Uploading(lastFailure));
        }

        videoKey = videoSlot.Key;
        posterKey = posterSlot.Key;
        uploadedAtUtc = DateTime.UtcNow;
        return null;
    }

    private bool UploadIsFresh() =>
        videoKey is not null && posterKey is not null && DateTime.UtcNow - uploadedAtUtc < UploadReuseWindow;

    private void ForgetUpload()
    {
        videoKey = null;
        posterKey = null;
    }

    private void Enter(ReelShareStage stage, float progress)
    {
        progressSink.Report(progress);
        state = new ReelShareState(stage, default, null);
    }

    private static ReelShareState Failed(ReelShareFailure failure) => new(ReelShareStage.Failed, failure, null);

    private static long FileBytes(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0L;
        }
    }

    private void DeleteConverted()
    {
        var path = convertedPath;
        convertedPath = null;
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            AepLog.Warning(exception, "[Reels] could not delete a converted reel");
        }
        catch (UnauthorizedAccessException exception)
        {
            AepLog.Warning(exception, "[Reels] could not delete a converted reel");
        }
    }

    private sealed class ProgressSink : IProgress<float>
    {
        private float value;

        public float Value => Volatile.Read(ref value);

        public void Report(float progress) => Volatile.Write(ref value, Math.Clamp(progress, 0f, 1f));
    }
}
