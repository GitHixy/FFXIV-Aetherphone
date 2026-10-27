using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Aetherphone.Core.Reels;

internal static class ReelTranscoder
{
    private const double RangeSlackSeconds = 0.05d;

    internal static Task<ReelTranscodeResult> TranscodeAsync(ReelTranscodeRequest request,
        IProgress<float>? progress, CancellationToken token) =>
        Task.Factory.StartNew(() => Run(request, progress, token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static MediaFoundationSession StartMediaFoundation()
    {
        try
        {
            return MediaFoundationSession.Start();
        }
        catch (DllNotFoundException exception)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing, exception.Message);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing, exception.Message);
        }
        catch (COMException exception)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing,
                $"MFStartup failed ({MediaFoundationVideo.Hex(exception.HResult)}).");
        }
    }

    internal static IMFSourceReader OpenReader(string path, bool advancedVideoProcessing)
    {
        var attributes = MediaFoundationApi.CreateAttributes(1);
        try
        {
            if (advancedVideoProcessing)
            {
                attributes.SetUINT32(MediaFoundationVideo.AdvancedVideoProcessing, 1);
            }

            MediaFoundationInterop.MFCreateSourceReaderFromURL(path, attributes, out var reader);
            return reader;
        }
        catch (COMException exception)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.UnreadableInput,
                $"Media Foundation cannot open the file ({MediaFoundationVideo.Hex(exception.HResult)}).");
        }
        catch (DllNotFoundException exception)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing, exception.Message);
        }
        finally
        {
            MediaFoundationVideo.Release(attributes);
        }
    }

    private static ReelTranscodeResult Run(ReelTranscodeRequest request, IProgress<float>? progress,
        CancellationToken token)
    {
        var report = new ReelTranscodeReport { HardwareEncoderRequested = request.AllowHardwareEncoder };
        var started = Stopwatch.GetTimestamp();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var partialPath = request.OutputPath + ".partial";
        try
        {
            CheckRange(request);
            if (!OperatingSystem.IsWindows())
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.MediaFoundationMissing,
                    "Media Foundation only exists on Windows.");
            }

            using (StartMediaFoundation())
            {
                using var session = new Session(request, partialPath, report, progress, token);
                session.Run();
            }

            Finish(partialPath, request.OutputPath, report);
            return new ReelTranscodeResult(ReelTranscodeFailure.None, null, report);
        }
        catch (ReelTranscodeException exception)
        {
            return Fail(exception.Failure, exception.Message, request.OutputPath, partialPath, report);
        }
        catch (OperationCanceledException)
        {
            return Fail(ReelTranscodeFailure.Cancelled, "The transcode was cancelled.", request.OutputPath,
                partialPath, report);
        }
        catch (DllNotFoundException exception)
        {
            return Fail(ReelTranscodeFailure.MediaFoundationMissing, exception.Message, request.OutputPath,
                partialPath, report);
        }
        catch (EntryPointNotFoundException exception)
        {
            return Fail(ReelTranscodeFailure.MediaFoundationMissing, exception.Message, request.OutputPath,
                partialPath, report);
        }
        catch (COMException exception)
        {
            return Fail(ReelTranscodeFailure.EncodeFailed,
                $"Media Foundation failed ({MediaFoundationVideo.Hex(exception.HResult)}): {exception.Message}",
                request.OutputPath, partialPath, report);
        }
        catch (IOException exception)
        {
            return Fail(ReelTranscodeFailure.EncodeFailed, exception.Message, request.OutputPath, partialPath,
                report);
        }
        catch (InvalidDataException exception)
        {
            return Fail(ReelTranscodeFailure.EncodeFailed, exception.Message, request.OutputPath, partialPath,
                report);
        }
        finally
        {
            report.ElapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            report.ManagedBytesAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
    }

    private static void CheckRange(in ReelTranscodeRequest request)
    {
        var length = request.EndSeconds - request.StartSeconds;
        if (request.StartSeconds < 0d || length <= 0d)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.InvalidRange,
                $"The trim {request.StartSeconds:F2} s to {request.EndSeconds:F2} s is empty.");
        }

        if (length > ReelFormat.MaxDurationSeconds + RangeSlackSeconds)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.InvalidRange,
                $"The trim is {length:F2} s; a reel is at most {ReelFormat.MaxDurationSeconds:F0} s.");
        }
    }

    private static void Finish(string partialPath, string outputPath, ReelTranscodeReport report)
    {
        using (var written = File.OpenRead(partialPath))
        {
            report.SinkLayout = Mp4Boxes.Inspect(written);
        }

        if (report.SinkLayout.MoovBeforeMdat)
        {
            File.Move(partialPath, outputPath, true);
        }
        else
        {
            using (var source = File.OpenRead(partialPath))
            using (var destination = File.Create(outputPath))
            {
                report.FastStartRewritten = Mp4FastStart.Rewrite(source, destination);
            }

            File.Delete(partialPath);
        }

        using (var output = File.OpenRead(outputPath))
        {
            report.Output = Mp4Boxes.Inspect(output);
            report.OutputBytes = output.Length;
        }

        if (!report.Output.MoovBeforeMdat)
        {
            throw new ReelTranscodeException(ReelTranscodeFailure.EncodeFailed,
                $"The output still has moov after mdat ({report.Output.BoxOrder}).");
        }
    }

    private static ReelTranscodeResult Fail(ReelTranscodeFailure failure, string detail, string outputPath,
        string partialPath, ReelTranscodeReport report)
    {
        DeleteQuietly(partialPath);
        DeleteQuietly(outputPath);
        return new ReelTranscodeResult(failure, detail, report);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct VideoGeometry(
        int CodedWidth,
        int CodedHeight,
        int Stride,
        PixelRect Visible,
        int ClockwiseRotation)
    {
        internal int DisplayWidth => ClockwiseRotation is 90 or 270 ? Visible.Height : Visible.Width;
        internal int DisplayHeight => ClockwiseRotation is 90 or 270 ? Visible.Width : Visible.Height;
    }

    private readonly ref struct LockedFrame
    {
        internal readonly IntPtr Scan0;
        internal readonly int Pitch;
        internal readonly long Length;
        private readonly IMFMediaBuffer buffer;
        private readonly IMF2DBuffer? surface;

        internal LockedFrame(IMFMediaBuffer buffer, in VideoGeometry geometry)
        {
            this.buffer = buffer;
            surface = buffer as IMF2DBuffer;
            if (surface is not null)
            {
                surface.Lock2D(out Scan0, out Pitch);
                Length = (long)Pitch * geometry.CodedHeight * 3 / 2;
                return;
            }

            buffer.Lock(out Scan0, out _, out var current);
            Pitch = geometry.Stride;
            Length = current;
        }

        public void Dispose()
        {
            if (surface is not null)
            {
                surface.Unlock2D();
                return;
            }

            buffer.Unlock();
        }
    }

    private sealed class Session : IDisposable
    {
        private const int ProgressEveryFrames = 15;
        private const int PreferredSampleRate = 48000;
        private const int PreferredChannels = 2;
        private const int PcmBits = 16;

        private readonly ReelTranscodeRequest request;
        private readonly string outputPath;
        private readonly ReelTranscodeReport report;
        private readonly IProgress<float>? progress;
        private readonly CancellationToken token;

        private IMFSourceReader reader = null!;
        private IMFSinkWriter writer = null!;
        private ReelStreams streams;
        private int writerVideo = -1;
        private int writerAudio = -1;
        private VideoGeometry geometry;
        private int nativeRotation;
        private int nativeWidth;
        private int nativeHeight;
        private ReelLayout layout;
        private ReelCadence cadence;
        private int audioSampleRate;
        private int audioChannels;
        private int audioBlockAlign;
        private long trimStart;
        private long trimLength;
        private long lastSlot = -1;
        private bool videoDone;
        private bool audioDone;

        internal Session(ReelTranscodeRequest request, string outputPath, ReelTranscodeReport report,
            IProgress<float>? progress, CancellationToken token)
        {
            this.request = request;
            this.outputPath = outputPath;
            this.report = report;
            this.progress = progress;
            this.token = token;
        }

        internal void Run()
        {
            var videoEncoders = MediaFoundationVideo.FindTransforms(MediaFoundationTransformCategories.VideoEncoder,
                MediaTypes.MFMediaType_Video, MediaFoundationVideo.VideoFormatH264);
            report.VideoEncoders = MediaFoundationVideo.Describe(videoEncoders);
            report.AudioEncoders = MediaFoundationVideo.Describe(MediaFoundationVideo.FindTransforms(
                MediaFoundationTransformCategories.AudioEncoder, MediaTypes.MFMediaType_Audio,
                AudioSubtypes.MFAudioFormat_AAC));
            if (videoEncoders.Length == 0)
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.EncoderMissing,
                    "No H.264 encoder is registered with Media Foundation.");
            }

            reader = OpenReader(request.InputPath, advancedVideoProcessing: true);
            var input = ReelMediaProbe.Describe(reader, request.InputPath, out streams);
            report.Input = input;
            if (streams.Video < 0)
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.NoVideo, "The file has no video track.");
            }

            PlanTrim(input.DurationSeconds);
            SelectStreams();
            ConfigureVideo(input);
            ConfigureAudio();
            layout = ReelLayout.Plan(geometry.DisplayWidth, geometry.DisplayHeight, request.Crop);
            report.Layout = layout;
            cadence = ReelCadence.For(input.FrameRateNumerator, input.FrameRateDenominator);
            report.Cadence = cadence;
            OpenWriter();
            report.DecoderChain = DescribeReaderChain();
            if (trimStart > 0)
            {
                MediaFoundationVideo.Seek(reader, trimStart);
            }

            Pump();
            var finalizing = Stopwatch.GetTimestamp();
            writer.DoFinalize();
            report.FinalizeSeconds = Stopwatch.GetElapsedTime(finalizing).TotalSeconds;
            progress?.Report(1f);
        }

        private void PlanTrim(double durationSeconds)
        {
            var end = durationSeconds > 0d ? Math.Min(request.EndSeconds, durationSeconds) : request.EndSeconds;
            if (end <= request.StartSeconds)
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.InvalidRange,
                    $"The trim starts at {request.StartSeconds:F2} s but the clip ends at {durationSeconds:F2} s.");
            }

            trimStart = Ticks(request.StartSeconds);
            trimLength = Ticks(end) - trimStart;
            report.TrimSeconds = trimLength / (double)ReelFormat.TicksPerSecond;
        }

        private static long Ticks(double seconds) => (long)Math.Round(seconds * ReelFormat.TicksPerSecond);

        private void SelectStreams()
        {
            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(streams.Video, true);
            if (streams.Audio >= 0)
            {
                reader.SetStreamSelection(streams.Audio, true);
            }
        }

        private void ConfigureVideo(in ReelMediaInfo input)
        {
            nativeRotation = input.RotationAttribute;
            nativeWidth = input.CodedWidth;
            nativeHeight = input.CodedHeight;
            var type = MediaFoundationApi.CreateMediaType();
            try
            {
                type.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
                type.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, MediaFoundationVideo.VideoFormatNv12);
                reader.SetCurrentMediaType(streams.Video, IntPtr.Zero, type);
            }
            catch (COMException exception)
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.DecoderMissing,
                    $"Windows cannot decode {input.VideoCodec} video to NV12 ({MediaFoundationVideo.Hex(exception.HResult)}).");
            }
            finally
            {
                MediaFoundationVideo.Release(type);
            }

            geometry = ReadGeometry();
        }

        private VideoGeometry ReadGeometry()
        {
            reader.GetCurrentMediaType(streams.Video, out var type);
            try
            {
                _ = MediaFoundationVideo.TryGetUInt64(type, MediaFoundationVideo.FrameSize, out var packed);
                var (width, height) = MediaFoundationVideo.Unpack(packed);
                var visible = MediaFoundationVideo.TryGetAperture(type, out var aperture)
                    ? aperture
                    : new PixelRect(0, 0, width, height);
                var stride = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationVideo.DefaultStride, out var found)
                    && found > 0
                        ? found
                        : width;
                var hasRotation = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationVideo.VideoRotation,
                    out var outputRotation);
                var readerRotated = !hasRotation && (nativeRotation is 90 or 270) && width == nativeHeight
                    && height == nativeWidth;
                var attribute = hasRotation ? outputRotation : readerRotated ? 0 : nativeRotation;
                var clockwise = (360 - attribute) % 360;
                report.Rotation =
                    $"native attribute {nativeRotation}, output attribute {(hasRotation ? outputRotation.ToString() : "absent")}, applied {clockwise} clockwise";
                return new VideoGeometry(width, height, stride, visible, clockwise);
            }
            finally
            {
                MediaFoundationVideo.Release(type);
            }
        }

        private void RefreshGeometry()
        {
            var refreshed = ReadGeometry();
            if (refreshed.DisplayWidth != geometry.DisplayWidth || refreshed.DisplayHeight != geometry.DisplayHeight)
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.EncodeFailed,
                    $"The video changed size mid-stream ({geometry.DisplayWidth}x{geometry.DisplayHeight} to {refreshed.DisplayWidth}x{refreshed.DisplayHeight}).");
            }

            geometry = refreshed;
        }

        private void ConfigureAudio()
        {
            if (streams.Audio < 0)
            {
                audioDone = true;
                return;
            }

            if (!TrySetAudioType(MediaFoundationApi.CreateMediaTypeFromWaveFormat(
                    new WaveFormat(PreferredSampleRate, PcmBits, PreferredChannels)))
                && !TrySetAudioType(PartialPcmType()))
            {
                DropAudio("Windows cannot decode the audio track to PCM.");
                return;
            }

            reader.GetCurrentMediaType(streams.Audio, out var type);
            try
            {
                _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND,
                    out audioSampleRate);
                _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS,
                    out audioChannels);
                _ = MediaFoundationVideo.TryGetUInt32(type, MediaFoundationAttributes.MF_MT_AUDIO_BLOCK_ALIGNMENT,
                    out audioBlockAlign);
            }
            finally
            {
                MediaFoundationVideo.Release(type);
            }

            if (audioSampleRate is not (44100 or 48000) || audioChannels is not (1 or 2) || audioBlockAlign <= 0)
            {
                DropAudio(
                    $"The AAC encoder takes 44.1 or 48 kHz mono or stereo; the track decodes to {audioSampleRate} Hz, {audioChannels} channels.");
                return;
            }

            report.HasAudio = true;
        }

        private static IMFMediaType PartialPcmType()
        {
            var type = MediaFoundationApi.CreateMediaType();
            type.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Audio);
            type.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, AudioSubtypes.MFAudioFormat_PCM);
            type.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE, PcmBits);
            return type;
        }

        private bool TrySetAudioType(IMFMediaType type)
        {
            try
            {
                reader.SetCurrentMediaType(streams.Audio, IntPtr.Zero, type);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
            finally
            {
                MediaFoundationVideo.Release(type);
            }
        }

        private void DropAudio(string reason)
        {
            report.AudioNote = reason;
            report.HasAudio = false;
            reader.SetStreamSelection(streams.Audio, false);
            streams = streams with { Audio = -1 };
            audioDone = true;
        }

        private void OpenWriter()
        {
            var attempts = new StringBuilder();
            var withAudio = streams.Audio >= 0;
            var opened = (request.AllowHardwareEncoder && TryOpenWriter(true, withAudio, attempts))
                || TryOpenWriter(false, withAudio, attempts);
            if (!opened && withAudio && TryOpenWriter(false, false, attempts))
            {
                opened = true;
                DropAudio("The AAC encoder refused the track.");
            }

            report.WriterAttempts = attempts.ToString();
            if (!opened)
            {
                throw new ReelTranscodeException(ReelTranscodeFailure.EncoderMissing,
                    "No H.264 encoder accepted the frames: " + report.WriterAttempts);
            }
        }

        private bool TryOpenWriter(bool hardware, bool withAudio, StringBuilder attempts)
        {
            var label = (hardware ? "hardware allowed" : "software only") + (withAudio ? " with audio" : " video only");
            var attributes = MediaFoundationApi.CreateAttributes(3);
            IMFSinkWriter? created = null;
            var stage = "create";
            try
            {
                attributes.SetUINT32(MediaFoundationAttributes.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1 : 0);
                attributes.SetGUID(MediaFoundationAttributes.MF_TRANSCODE_CONTAINERTYPE,
                    TranscodeContainerTypes.MFTranscodeContainerType_MPEG4);
                attributes.SetUINT32(MediaFoundationVideo.MoovBeforeMdat, 1);
                DeleteQuietly(outputPath);
                MediaFoundationInterop.MFCreateSinkWriterFromURL(outputPath, null, attributes, out created);
                stage = "video stream";
                var video = AddVideoStream(created);
                var audio = -1;
                if (withAudio)
                {
                    stage = "audio stream";
                    audio = AddAudioStream(created);
                }

                stage = "begin writing";
                created.BeginWriting();
                writer = created;
                writerVideo = video;
                writerAudio = audio;
                report.EncoderChain = DescribeWriterChain(created, video);
                attempts.Append(label).Append(": opened. ");
                return true;
            }
            catch (COMException exception)
            {
                attempts.Append(label).Append(": failed at ").Append(stage).Append(" (")
                    .Append(MediaFoundationVideo.Hex(exception.HResult)).Append("). ");
                MediaFoundationVideo.Release(created);
                return false;
            }
            finally
            {
                MediaFoundationVideo.Release(attributes);
            }
        }

        private int AddVideoStream(IMFSinkWriter target)
        {
            var output = MediaFoundationApi.CreateMediaType();
            var input = MediaFoundationApi.CreateMediaType();
            try
            {
                output.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
                output.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, MediaFoundationVideo.VideoFormatH264);
                output.SetUINT32(MediaFoundationAttributes.MF_MT_AVG_BITRATE, ReelFormat.VideoBitsPerSecond);
                output.SetUINT32(MediaFoundationVideo.Mpeg2Profile, MediaFoundationVideo.H264HighProfile);
                SetFrameShape(output);
                target.AddStream(output, out var stream);

                input.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
                input.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, MediaFoundationVideo.VideoFormatNv12);
                input.SetUINT32(MediaFoundationVideo.DefaultStride, layout.Width);
                SetFrameShape(input);
                target.SetInputMediaType(stream, input, null);
                return stream;
            }
            finally
            {
                MediaFoundationVideo.Release(input);
                MediaFoundationVideo.Release(output);
            }
        }

        private void SetFrameShape(IMFMediaType type)
        {
            type.SetUINT32(MediaFoundationVideo.InterlaceMode, MediaFoundationVideo.ProgressiveInterlace);
            type.SetUINT64(MediaFoundationVideo.FrameSize, MediaFoundationVideo.Pack(layout.Width, layout.Height));
            type.SetUINT64(MediaFoundationVideo.FrameRate,
                MediaFoundationVideo.Pack(cadence.Numerator, cadence.Denominator));
            type.SetUINT64(MediaFoundationVideo.PixelAspectRatio, MediaFoundationVideo.Pack(1, 1));
        }

        private int AddAudioStream(IMFSinkWriter target)
        {
            var output = MediaFoundationApi.CreateMediaType();
            var input = MediaFoundationApi.CreateMediaTypeFromWaveFormat(
                new WaveFormat(audioSampleRate, PcmBits, audioChannels));
            try
            {
                output.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Audio);
                output.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, AudioSubtypes.MFAudioFormat_AAC);
                output.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE, PcmBits);
                output.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND, audioSampleRate);
                output.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS, audioChannels);
                output.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_AVG_BYTES_PER_SECOND,
                    ReelFormat.AudioBytesPerSecond);
                target.AddStream(output, out var stream);
                target.SetInputMediaType(stream, input, null);
                return stream;
            }
            finally
            {
                MediaFoundationVideo.Release(input);
                MediaFoundationVideo.Release(output);
            }
        }

        private string DescribeReaderChain()
        {
            if (reader is not IMFSourceReaderEx extended)
            {
                return "unknown (no IMFSourceReaderEx)";
            }

            var chain = new StringBuilder();
            for (var transformIndex = 0; ; transformIndex++)
            {
                IMFTransform transform;
                Guid category;
                try
                {
                    extended.GetTransformForStream(streams.Video, transformIndex, out category, out transform);
                }
                catch (COMException)
                {
                    break;
                }

                AppendTransform(chain, category, transform);
            }

            return chain.Length == 0 ? "none reported" : chain.ToString();
        }

        private static string DescribeWriterChain(IMFSinkWriter target, int stream)
        {
            if (target is not IMFSinkWriterEx extended)
            {
                return "unknown (no IMFSinkWriterEx)";
            }

            var chain = new StringBuilder();
            for (var transformIndex = 0; ; transformIndex++)
            {
                IMFTransform transform;
                Guid category;
                try
                {
                    extended.GetTransformForStream(stream, transformIndex, out category, out transform);
                }
                catch (COMException)
                {
                    break;
                }

                AppendTransform(chain, category, transform);
            }

            return chain.Length == 0 ? "none reported" : chain.ToString();
        }

        private static void AppendTransform(StringBuilder chain, Guid category, IMFTransform transform)
        {
            try
            {
                if (chain.Length > 0)
                {
                    chain.Append(" -> ");
                }

                chain.Append(CategoryName(category)).Append(": ").Append(MediaFoundationVideo.DescribeTransform(transform));
            }
            finally
            {
                MediaFoundationVideo.Release(transform);
            }
        }

        private static string CategoryName(Guid category)
        {
            if (category == MediaFoundationTransformCategories.VideoDecoder)
            {
                return "decoder";
            }

            if (category == MediaFoundationTransformCategories.VideoProcessor)
            {
                return "processor";
            }

            if (category == MediaFoundationTransformCategories.VideoEncoder)
            {
                return "encoder";
            }

            return "transform";
        }

        private void Pump()
        {
            while (!videoDone || !audioDone)
            {
                token.ThrowIfCancellationRequested();
                var reading = Stopwatch.GetTimestamp();
                reader.ReadSample(MediaFoundationVideo.AnyStream, 0, out var streamIndex, out var flags,
                    out var timestamp, out var sample);
                report.ReadSeconds += Stopwatch.GetElapsedTime(reading).TotalSeconds;
                try
                {
                    var status = (int)flags;
                    if ((status & MediaFoundationVideo.ReaderError) != 0)
                    {
                        throw new ReelTranscodeException(ReelTranscodeFailure.EncodeFailed,
                            "The source reader stopped with an error while decoding.");
                    }

                    if (streamIndex == streams.Video)
                    {
                        OnVideo(status, (long)timestamp, sample);
                    }
                    else if (streamIndex == streams.Audio)
                    {
                        OnAudio(status, (long)timestamp, sample);
                    }
                }
                finally
                {
                    MediaFoundationVideo.Release(sample);
                }
            }
        }

        private void OnVideo(int status, long timestamp, IMFSample? sample)
        {
            if ((status & MediaFoundationVideo.ReaderCurrentTypeChanged) != 0)
            {
                RefreshGeometry();
            }

            if ((status & MediaFoundationVideo.ReaderEndOfStream) != 0)
            {
                videoDone = true;
            }

            if (sample is null || videoDone)
            {
                return;
            }

            report.FramesDecoded++;
            var rebased = timestamp - trimStart;
            if (rebased >= trimLength)
            {
                videoDone = true;
                reader.SetStreamSelection(streams.Video, false);
                return;
            }

            if (rebased < 0)
            {
                return;
            }

            var slot = cadence.SlotOf(rebased);
            if (slot <= lastSlot)
            {
                return;
            }

            lastSlot = slot;
            WriteVideoFrame(sample, slot);
            if (report.FramesWritten % ProgressEveryFrames == 0)
            {
                progress?.Report(Math.Clamp((float)((double)rebased / trimLength), 0f, 1f));
            }
        }

        private unsafe void WriteVideoFrame(IMFSample source, long slot)
        {
            var frameBytes = layout.Width * layout.Height * 3 / 2;
            var lumaBytes = layout.Width * layout.Height;
            IMFMediaBuffer? input = null;
            IMFMediaBuffer? output = null;
            IMFSample? frame = null;
            try
            {
                source.GetBufferCount(out var bufferCount);
                if (bufferCount == 1)
                {
                    source.GetBufferByIndex(0, out input);
                }
                else
                {
                    source.ConvertToContiguousBuffer(out input);
                }

                output = MediaFoundationApi.CreateMemoryBuffer(frameBytes);
                output.Lock(out var outputPointer, out _, out _);
                try
                {
                    using var locked = new LockedFrame(input, geometry);
                    var sourceLumaBytes = locked.Pitch * geometry.CodedHeight;
                    if (locked.Pitch <= 0 || locked.Length < sourceLumaBytes * 3L / 2)
                    {
                        throw new ReelTranscodeException(ReelTranscodeFailure.EncodeFailed,
                            $"A decoded frame holds {locked.Length} bytes at pitch {locked.Pitch}, less than {geometry.CodedWidth}x{geometry.CodedHeight} NV12 needs.");
                    }

                    var decoded = new Nv12Source(new ReadOnlySpan<byte>((void*)locked.Scan0, sourceLumaBytes),
                        new ReadOnlySpan<byte>((void*)(locked.Scan0 + sourceLumaBytes), sourceLumaBytes / 2),
                        locked.Pitch, geometry.Visible);
                    var target = new Nv12Target(new Span<byte>((void*)outputPointer, lumaBytes),
                        new Span<byte>((void*)(outputPointer + lumaBytes), lumaBytes / 2), layout.Width,
                        layout.Height);
                    var scaling = Stopwatch.GetTimestamp();
                    Nv12Scaler.Render(decoded, target, layout, geometry.ClockwiseRotation);
                    report.ScaleSeconds += Stopwatch.GetElapsedTime(scaling).TotalSeconds;
                }
                finally
                {
                    output.Unlock();
                }

                output.SetCurrentLength(frameBytes);
                frame = MediaFoundationApi.CreateSample();
                frame.AddBuffer(output);
                var time = cadence.TimeOf(slot);
                frame.SetSampleTime(time);
                frame.SetSampleDuration(cadence.TimeOf(slot + 1) - time);
                Write(writerVideo, frame);
                report.FramesWritten++;
            }
            finally
            {
                MediaFoundationVideo.Release(frame);
                MediaFoundationVideo.Release(output);
                MediaFoundationVideo.Release(input);
            }
        }

        private void OnAudio(int status, long timestamp, IMFSample? sample)
        {
            if ((status & MediaFoundationVideo.ReaderEndOfStream) != 0)
            {
                audioDone = true;
            }

            if (sample is null || audioDone)
            {
                return;
            }

            var start = timestamp - trimStart;
            if (start >= trimLength)
            {
                FinishAudio();
                return;
            }

            sample.GetTotalLength(out var bytes);
            var frames = bytes / audioBlockAlign;
            var firstFrame = start < 0 ? FramesCeiling(-start) : 0;
            var endFrame = (int)Math.Min(frames, FramesFloor(trimLength - start));
            if (firstFrame >= endFrame)
            {
                return;
            }

            if (firstFrame == 0 && endFrame == frames)
            {
                sample.SetSampleTime(start);
                Write(writerAudio, sample);
                report.AudioBytesWritten += bytes;
                return;
            }

            WriteAudioSlice(sample, firstFrame, endFrame, start);
            if (endFrame < frames)
            {
                FinishAudio();
            }
        }

        private void Write(int stream, IMFSample sample)
        {
            var writing = Stopwatch.GetTimestamp();
            writer.WriteSample(stream, sample);
            report.WriteSeconds += Stopwatch.GetElapsedTime(writing).TotalSeconds;
        }

        private void FinishAudio()
        {
            audioDone = true;
            reader.SetStreamSelection(streams.Audio, false);
        }

        private int FramesCeiling(long ticks) =>
            (int)((ticks * audioSampleRate + ReelFormat.TicksPerSecond - 1) / ReelFormat.TicksPerSecond);

        private long FramesFloor(long ticks) => ticks * audioSampleRate / ReelFormat.TicksPerSecond;

        private long TicksOf(int frames) => frames * ReelFormat.TicksPerSecond / audioSampleRate;

        private unsafe void WriteAudioSlice(IMFSample sample, int firstFrame, int endFrame, long start)
        {
            var length = (endFrame - firstFrame) * audioBlockAlign;
            IMFMediaBuffer? source = null;
            IMFMediaBuffer? slice = null;
            IMFSample? sliced = null;
            try
            {
                sample.ConvertToContiguousBuffer(out source);
                slice = MediaFoundationApi.CreateMemoryBuffer(length);
                source.Lock(out var sourcePointer, out _, out _);
                try
                {
                    slice.Lock(out var slicePointer, out _, out _);
                    try
                    {
                        Buffer.MemoryCopy((void*)(sourcePointer + firstFrame * audioBlockAlign), (void*)slicePointer,
                            length, length);
                    }
                    finally
                    {
                        slice.Unlock();
                    }
                }
                finally
                {
                    source.Unlock();
                }

                slice.SetCurrentLength(length);
                sliced = MediaFoundationApi.CreateSample();
                sliced.AddBuffer(slice);
                sliced.SetSampleTime(start + TicksOf(firstFrame));
                sliced.SetSampleDuration(TicksOf(endFrame - firstFrame));
                Write(writerAudio, sliced);
                report.AudioBytesWritten += length;
            }
            finally
            {
                MediaFoundationVideo.Release(sliced);
                MediaFoundationVideo.Release(slice);
                MediaFoundationVideo.Release(source);
            }
        }

        public void Dispose()
        {
            MediaFoundationVideo.Release(writer);
            MediaFoundationVideo.Release(reader);
        }
    }
}
