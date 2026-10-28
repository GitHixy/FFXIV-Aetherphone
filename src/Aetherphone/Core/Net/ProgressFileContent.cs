using System.Net;

namespace Aetherphone.Core.Net;

// Streams a file as a request body with a known length, reporting how much has gone out.
internal sealed class ProgressFileContent : HttpContent
{
    public const int ChunkBytes = 64 * 1024;

    private readonly FileStream file;
    private readonly IProgress<float> progress;

    public ProgressFileContent(FileStream file, IProgress<float> progress)
    {
        this.file = file;
        this.progress = progress;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        var total = file.Length;
        var buffer = new byte[ChunkBytes];
        file.Position = 0;
        long sent = 0;
        progress.Report(0f);
        int read;
        while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sent += read;
            progress.Report(total > 0 ? (float)((double)sent / total) : 1f);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = file.Length;
        return true;
    }
}
