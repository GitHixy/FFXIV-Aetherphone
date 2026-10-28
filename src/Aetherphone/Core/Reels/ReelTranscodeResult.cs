namespace Aetherphone.Core.Reels;

internal enum ReelTranscodeFailure : byte
{
    None,
    MediaFoundationMissing,
    EncoderMissing,
    DecoderMissing,
    UnreadableInput,
    NoVideo,
    InvalidRange,
    EncodeFailed,
    Cancelled,
    UnknownDuration,
}

internal readonly record struct ReelTranscodeRequest(
    string InputPath,
    string OutputPath,
    double StartSeconds,
    double EndSeconds,
    ReelCrop? Crop,
    bool AllowHardwareEncoder);

internal sealed class ReelTranscodeException : Exception
{
    internal ReelTranscodeException(ReelTranscodeFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    internal ReelTranscodeFailure Failure { get; }
}

internal sealed class ReelTranscodeReport
{
    internal ReelMediaInfo Input { get; set; }
    internal ReelLayout Layout { get; set; }
    internal ReelCadence Cadence { get; set; }
    internal double TrimSeconds { get; set; }
    internal long SourceVideoBitsPerSecond { get; set; }
    internal int VideoBitsPerSecond { get; set; } = ReelFormat.VideoBitsPerSecond;
    internal string VideoEncoders { get; set; } = "not checked";
    internal string AudioEncoders { get; set; } = "not checked";
    internal string WriterAttempts { get; set; } = string.Empty;
    internal string EncoderChain { get; set; } = "unknown";
    internal string DecoderChain { get; set; } = "unknown";
    internal string Rotation { get; set; } = "none";
    internal bool HardwareEncoderRequested { get; set; }
    internal bool HasAudio { get; set; }
    internal string? AudioNote { get; set; }
    internal int FramesDecoded { get; set; }
    internal int FramesWritten { get; set; }
    internal long AudioBytesWritten { get; set; }
    internal Mp4Layout SinkLayout { get; set; }
    internal bool FastStartRewritten { get; set; }
    internal Mp4Layout Output { get; set; }
    internal long OutputBytes { get; set; }
    internal double ReadSeconds { get; set; }
    internal double ScaleSeconds { get; set; }
    internal int ScaleWorkers { get; set; }
    internal int CopiedFrames { get; set; }
    internal int SampledFrames { get; set; }
    internal double WriteSeconds { get; set; }
    internal double FinalizeSeconds { get; set; }
    internal double ElapsedSeconds { get; set; }
    internal long ManagedBytesAllocated { get; set; }
}

internal sealed record ReelTranscodeResult(ReelTranscodeFailure Failure, string? Detail, ReelTranscodeReport Report)
{
    internal bool Succeeded => Failure == ReelTranscodeFailure.None;
}
