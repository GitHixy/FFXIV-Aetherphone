using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Media;
using Aetherphone.Core.Net;
using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelShareJobTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "AetherphoneReelShareTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    private static ReelShareRequest Request(string caption = "  first reel #kugane  ", string? alt = null) =>
        new("C:/clips/source.mkv", 2d, 47d, 10d, ReelCrop.FillPortrait(1920, 1080, 0.25f), caption,
            new[] { new PhotoTagInput("user-1", 0, 0.5f, 0.5f) }, new[] { "collab-1" }, alt, true);

    private ReelShareJob Job(FakeReelMedia media, FakePublisher publisher, ReelShareRequest? request = null) =>
        new(media, publisher, folder, request ?? Request(), true);

    [Fact]
    public async Task ConvertsThenUploadsBothFilesThenCreatesTheReel()
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher();
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;

        Assert.Equal(ReelShareStage.Done, job.State.Stage);
        Assert.Same(publisher.Created, job.State.Created);
        Assert.Equal(new[] { "transcode", "poster" }, media.Calls);
        Assert.Equal(new[]
        {
            "reserve video/mp4 reel", "file video/mp4", "reserve image/jpeg reel-poster", "bytes image/jpeg", "create",
        }, publisher.Calls);
        var sent = publisher.LastCreate!;
        Assert.Equal("first reel #kugane", sent.Caption);
        Assert.Equal("reels/me/video-1.mp4", sent.VideoKey);
        Assert.Equal("reels/me/poster-2.jpg", sent.PosterKey);
        Assert.Equal("collab-1", Assert.Single(sent.CollaboratorIds!));
        Assert.Null(sent.AltText);
        Assert.True(sent.Sensitive);
        Assert.Equal(2d, media.LastTranscode!.Value.StartSeconds);
        Assert.Equal(47d, media.LastTranscode.Value.EndSeconds);
        Assert.Equal(Request().Crop, media.LastTranscode.Value.Crop);
        Assert.Equal(10d, media.LastPosterSeconds);
        Assert.False(File.Exists(media.LastTranscode.Value.OutputPath));
    }

    [Theory]
    [InlineData((int)ReelTranscodeFailure.MediaFoundationMissing, false)]
    [InlineData((int)ReelTranscodeFailure.EncoderMissing, false)]
    [InlineData((int)ReelTranscodeFailure.DecoderMissing, false)]
    [InlineData((int)ReelTranscodeFailure.UnreadableInput, false)]
    [InlineData((int)ReelTranscodeFailure.NoVideo, false)]
    [InlineData((int)ReelTranscodeFailure.InvalidRange, false)]
    [InlineData((int)ReelTranscodeFailure.UnknownDuration, false)]
    [InlineData((int)ReelTranscodeFailure.EncodeFailed, true)]
    public async Task AConversionFailureStopsBeforeAnyUpload(int failureValue, bool retryable)
    {
        var failure = (ReelTranscodeFailure)failureValue;
        var media = new FakeReelMedia { TranscodeFailure = failure };
        var publisher = new FakePublisher();
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;

        Assert.Equal(ReelShareStage.Failed, job.State.Stage);
        Assert.Equal(ReelShareStep.Convert, job.State.Failure.Step);
        Assert.Equal(failure, job.State.Failure.Transcode);
        Assert.Equal(retryable, job.State.Failure.Retryable);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    public async Task ACoverFailureStopsBeforeAnyUploadAndCanBeRetried()
    {
        var media = new FakeReelMedia { PosterFailure = ReelTranscodeFailure.EncodeFailed };
        var publisher = new FakePublisher();
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;

        Assert.Equal(ReelShareStep.Cover, job.State.Failure.Step);
        Assert.Empty(publisher.Calls);

        media.PosterFailure = ReelTranscodeFailure.None;
        Assert.True(job.Retry());
        await job.Running;

        Assert.Equal(ReelShareStage.Done, job.State.Stage);
        Assert.Equal(1, media.Count("transcode"));
    }

    [Fact]
    public async Task CancellingWhileConvertingUploadsNothingAndClearsTheFile()
    {
        var media = new FakeReelMedia { HoldTranscode = true };
        var publisher = new FakePublisher();
        using var job = Job(media, publisher);

        job.Start();
        await media.TranscodeStarted.Task;
        Assert.True(job.State.Cancellable);
        job.Cancel();
        await job.Running;

        Assert.Equal(ReelShareStage.Cancelled, job.State.Stage);
        Assert.Empty(publisher.Calls);
        Assert.False(Directory.Exists(folder) && Directory.GetFiles(folder).Length > 0);
    }

    [Fact]
    public async Task AnOfflineUploadIsRetriedWithoutConvertingAgain()
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher { FailVideoUploads = 1 };
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;

        Assert.Equal(ReelShareStep.Upload, job.State.Failure.Step);
        Assert.True(job.State.Failure.Retryable);
        Assert.Equal(0, publisher.Count("create"));

        Assert.True(job.Retry());
        await job.Running;

        Assert.Equal(ReelShareStage.Done, job.State.Stage);
        Assert.Equal(1, media.Count("transcode"));
        Assert.Equal(2, publisher.Count("file video/mp4"));
    }

    [Fact]
    public async Task ACreateLostInTransitRetriesWithTheSameUploads()
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher();
        publisher.CreateFailures.Enqueue(AepFailure.Transport(AepFailureKind.Timeout));
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;
        Assert.Equal(ReelShareStep.Create, job.State.Failure.Step);
        Assert.True(job.State.Failure.Retryable);

        Assert.True(job.Retry());
        await job.Running;

        Assert.Equal(ReelShareStage.Done, job.State.Stage);
        Assert.Equal(1, publisher.Count("file video/mp4"));
        Assert.Equal(2, publisher.Count("create"));
    }

    [Fact]
    public async Task AnUploadInUseAfterALostCreateMeansTheFirstCreateLanded()
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher();
        publisher.CreateFailures.Enqueue(AepFailure.Transport(AepFailureKind.Timeout));
        publisher.CreateFailures.Enqueue(Server(409, ReelErrorCodes.MediaInUse));
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;
        Assert.True(job.Retry());
        await job.Running;

        Assert.Equal(ReelShareStage.Done, job.State.Stage);
        Assert.Null(job.State.Created);
    }

    [Fact]
    public async Task AMissingVideoIsUploadedAgainOnRetry()
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher();
        publisher.CreateFailures.Enqueue(Server(400, ReelErrorCodes.MissingVideo));
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;
        Assert.True(job.State.Failure.Retryable);
        Assert.True(job.Retry());
        await job.Running;

        Assert.Equal(ReelShareStage.Done, job.State.Stage);
        Assert.Equal(2, publisher.Count("file video/mp4"));
    }

    [Theory]
    [InlineData(ReelErrorCodes.NotMp4)]
    [InlineData(ReelErrorCodes.NotFaststart)]
    [InlineData(ReelErrorCodes.VideoCodec)]
    [InlineData(ReelErrorCodes.AudioCodec)]
    [InlineData(ReelErrorCodes.TooLong)]
    [InlineData(ReelErrorCodes.Aspect)]
    [InlineData(ReelErrorCodes.Resolution)]
    [InlineData(ReelErrorCodes.PosterInvalid)]
    [InlineData(ReelErrorCodes.MediaInUse)]
    public async Task AReelTheServerRefusesIsNotRetried(string code)
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher();
        publisher.CreateFailures.Enqueue(Server(400, code));
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;

        Assert.Equal(ReelShareStage.Failed, job.State.Stage);
        Assert.Equal(code, job.State.Failure.Network.Code);
        Assert.False(job.State.Failure.Retryable);
        Assert.False(job.Retry());
    }

    [Fact]
    public async Task AnOversizedConversionIsRefusedBeforeUploading()
    {
        var media = new FakeReelMedia { OutputBytes = ReelShareJob.MaxVideoBytes + 1 };
        var publisher = new FakePublisher();
        using var job = Job(media, publisher);

        job.Start();
        await job.Running;

        Assert.Equal(ReelErrorCodes.TooBig, job.State.Failure.Network.Code);
        Assert.False(job.State.Failure.Retryable);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    public async Task ABlankAltTextIsNotSent()
    {
        var media = new FakeReelMedia();
        var publisher = new FakePublisher();
        using var job = Job(media, publisher, Request(alt: "   "));

        job.Start();
        await job.Running;

        Assert.Null(publisher.LastCreate!.AltText);
    }

    private static PostDto Gram(string id) =>
        new(id, "me", "Me", "World", "Me", "me", "first reel", 1_700_000_000, new int[6], 0, -1, 1,
            "https://media/poster.jpg", 608, 1080, null, 0, false);

    private static AepFailure Server(int status, string code) => new(AepFailureKind.Server, status, code, null, null, null);

    private sealed class FakeReelMedia : IReelMedia
    {
        private readonly List<string> calls = new();

        public ReelTranscodeFailure TranscodeFailure { get; init; }
        public ReelTranscodeFailure PosterFailure { get; set; }
        public bool HoldTranscode { get; init; }
        public long OutputBytes { get; init; } = 64;
        public TaskCompletionSource TranscodeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReelTranscodeRequest? LastTranscode { get; private set; }
        public double LastPosterSeconds { get; private set; }

        public string[] Calls
        {
            get
            {
                lock (calls)
                {
                    return calls.ToArray();
                }
            }
        }

        public int Count(string call) => Calls.Count(entry => entry == call);

        public Task<ReelProbeResult> ProbeAsync(string path, CancellationToken token) =>
            Task.FromResult(new ReelProbeResult(ReelTranscodeFailure.None, default));

        public Task<ReelFramesResult> ThumbnailsAsync(string path, double[] seconds, int longSide, bool exact,
            CancellationToken token) =>
            Task.FromResult(new ReelFramesResult(ReelTranscodeFailure.None, Array.Empty<PixelImage>()));

        public Task<ReelPosterResult> PosterAsync(string path, double seconds, ReelCrop? crop, CancellationToken token)
        {
            Record("poster");
            LastPosterSeconds = seconds;
            return Task.FromResult(PosterFailure == ReelTranscodeFailure.None
                ? new ReelPosterResult(ReelTranscodeFailure.None, new byte[] { 0xFF, 0xD8, 0xFF }, 608, 1080)
                : new ReelPosterResult(PosterFailure, Array.Empty<byte>(), 0, 0));
        }

        public async Task<ReelTranscodeResult> TranscodeAsync(ReelTranscodeRequest request, IProgress<float>? progress,
            CancellationToken token)
        {
            Record("transcode");
            LastTranscode = request;
            TranscodeStarted.TrySetResult();
            if (HoldTranscode)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    return new ReelTranscodeResult(ReelTranscodeFailure.Cancelled, "cancelled", new ReelTranscodeReport());
                }
            }

            if (TranscodeFailure != ReelTranscodeFailure.None)
            {
                return new ReelTranscodeResult(TranscodeFailure, "fake", new ReelTranscodeReport());
            }

            progress?.Report(0.5f);
            using (var file = File.Create(request.OutputPath))
            {
                file.SetLength(OutputBytes);
            }

            progress?.Report(1f);
            return new ReelTranscodeResult(ReelTranscodeFailure.None, null, new ReelTranscodeReport());
        }

        private void Record(string call)
        {
            lock (calls)
            {
                calls.Add(call);
            }
        }
    }

    private sealed class FakePublisher : IReelPublisher
    {
        private readonly List<string> calls = new();
        private int slot;

        public int FailVideoUploads { get; set; }
        public Queue<AepFailure> CreateFailures { get; } = new();
        public CreateReelRequest? LastCreate { get; private set; }
        public PostDto? Created { get; private set; }

        public string[] Calls
        {
            get
            {
                lock (calls)
                {
                    return calls.ToArray();
                }
            }
        }

        public int Count(string call) => Calls.Count(entry => entry == call);

        public Task<UploadUrlResponse?> ReserveAsync(string contentType, string scope, CancellationToken token,
            Action<AepFailure> onFailure)
        {
            Record($"reserve {contentType} {scope}");
            slot++;
            var key = scope == ReelShareJob.VideoScope ? $"reels/me/video-{slot}.mp4" : $"reels/me/poster-{slot}.jpg";
            return Task.FromResult<UploadUrlResponse?>(new UploadUrlResponse(key, "https://upload/" + key,
                "https://media/" + key));
        }

        public Task<bool> UploadFileAsync(string uploadUrl, string path, string contentType, IProgress<float> progress,
            CancellationToken token, Action<AepFailure> onFailure)
        {
            Record($"file {contentType}");
            Assert.True(File.Exists(path));
            if (FailVideoUploads > 0)
            {
                FailVideoUploads--;
                onFailure(AepFailure.Transport(AepFailureKind.Offline));
                return Task.FromResult(false);
            }

            progress.Report(1f);
            return Task.FromResult(true);
        }

        public Task<bool> UploadBytesAsync(string uploadUrl, byte[] bytes, string contentType,
            CancellationToken token, Action<AepFailure> onFailure)
        {
            Record($"bytes {contentType}");
            return Task.FromResult(true);
        }

        public Task<PostDto?> CreateAsync(CreateReelRequest request, CancellationToken token,
            Action<AepFailure> onFailure)
        {
            Record("create");
            LastCreate = request;
            if (CreateFailures.Count > 0)
            {
                onFailure(CreateFailures.Dequeue());
                return Task.FromResult<PostDto?>(null);
            }

            Created = Gram("reel-1");
            return Task.FromResult<PostDto?>(Created);
        }

        private void Record(string call)
        {
            lock (calls)
            {
                calls.Add(call);
            }
        }
    }
}
