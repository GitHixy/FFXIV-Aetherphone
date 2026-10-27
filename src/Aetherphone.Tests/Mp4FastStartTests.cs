using System.Buffers.Binary;
using System.Text;
using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class Mp4FastStartTests
{
    private static readonly byte[][] Chunks =
    [
        Encoding.ASCII.GetBytes("AAAA"),
        Encoding.ASCII.GetBytes("BBBBBB"),
        Encoding.ASCII.GetBytes("CC"),
    ];

    [Fact]
    public void MovesMoovAheadOfMdatAndRepointsEveryChunk()
    {
        var original = BuildFile(wide: false, largeMediaHeader: false);

        var rewritten = Rewrite(original, out var changed);

        Assert.True(changed);
        using var stream = new MemoryStream(rewritten);
        var layout = Mp4Boxes.Inspect(stream);
        Assert.Equal("ftyp moov mdat", layout.BoxOrder);
        Assert.True(layout.MoovBeforeMdat);
        AssertChunksResolve(rewritten, wide: false);
    }

    [Fact]
    public void Repoints64BitOffsetsBehindALargeSizeMdat()
    {
        var original = BuildFile(wide: true, largeMediaHeader: true);

        var rewritten = Rewrite(original, out var changed);

        Assert.True(changed);
        Assert.Equal(original.Length, rewritten.Length);
        AssertChunksResolve(rewritten, wide: true);
    }

    [Fact]
    public void LeavesAFastStartFileAlone()
    {
        var original = BuildFile(wide: false, largeMediaHeader: false);
        var faststart = Rewrite(original, out _);

        var again = Rewrite(faststart, out var changed);

        Assert.False(changed);
        Assert.Empty(again);
    }

    [Fact]
    public void ReadsTheMovieDurationFromMvhd()
    {
        using var stream = new MemoryStream(BuildFile(wide: false, largeMediaHeader: false));

        var layout = Mp4Boxes.Inspect(stream);

        Assert.Equal("ftyp mdat moov", layout.BoxOrder);
        Assert.False(layout.MoovBeforeMdat);
        Assert.Equal(60d, layout.DurationSeconds, 6);
    }

    [Fact]
    public void RejectsABoxThatOverrunsTheFile()
    {
        var file = BuildFile(wide: false, largeMediaHeader: false);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(16), 1_000_000);

        Assert.Throws<InvalidDataException>(() => Mp4Boxes.Inspect(new MemoryStream(file)));
    }

    private static byte[] Rewrite(byte[] source, out bool changed)
    {
        using var input = new MemoryStream(source);
        using var output = new MemoryStream();
        changed = Mp4FastStart.Rewrite(input, output);
        return output.ToArray();
    }

    private static void AssertChunksResolve(byte[] file, bool wide)
    {
        var offsets = ReadChunkOffsets(file, wide);
        Assert.Equal(Chunks.Length, offsets.Count);
        for (var index = 0; index < Chunks.Length; index++)
        {
            Assert.Equal(Chunks[index], file.AsSpan((int)offsets[index], Chunks[index].Length).ToArray());
        }
    }

    private static List<long> ReadChunkOffsets(byte[] file, bool wide)
    {
        var tag = Encoding.ASCII.GetBytes(wide ? "co64" : "stco");
        var at = file.AsSpan().IndexOf(tag);
        Assert.True(at > 0);
        var count = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 8));
        var offsets = new List<long>();
        for (var index = 0; index < count; index++)
        {
            var entry = at + 12 + index * (wide ? 8 : 4);
            offsets.Add(wide
                ? (long)BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(entry))
                : BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(entry)));
        }

        return offsets;
    }

    private static byte[] BuildFile(bool wide, bool largeMediaHeader)
    {
        var fileType = Box("ftyp", Encoding.ASCII.GetBytes("isom"), new byte[] { 0, 0, 2, 0 });
        var payload = Chunks.SelectMany(chunk => chunk).ToArray();
        var mediaHeader = largeMediaHeader ? 16 : 8;
        var mediaData = largeMediaHeader ? LargeBox("mdat", payload) : Box("mdat", payload);

        var offsets = new List<long>();
        long cursor = fileType.Length + mediaHeader;
        foreach (var chunk in Chunks)
        {
            offsets.Add(cursor);
            cursor += chunk.Length;
        }

        var movie = Box("moov", MovieHeader(timescale: 1000, duration: 60_000),
            Box("trak", Box("mdia", Box("minf", Box("stbl", ChunkTable(offsets, wide))))));
        return fileType.Concat(mediaData).Concat(movie).ToArray();
    }

    private static byte[] MovieHeader(uint timescale, uint duration)
    {
        var body = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), duration);
        return Box("mvhd", body);
    }

    private static byte[] ChunkTable(List<long> offsets, bool wide)
    {
        var entrySize = wide ? 8 : 4;
        var body = new byte[8 + offsets.Count * entrySize];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), (uint)offsets.Count);
        for (var index = 0; index < offsets.Count; index++)
        {
            var slot = body.AsSpan(8 + index * entrySize);
            if (wide)
            {
                BinaryPrimitives.WriteUInt64BigEndian(slot, (ulong)offsets[index]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(slot, (uint)offsets[index]);
            }
        }

        return Box(wide ? "co64" : "stco", body);
    }

    private static byte[] Box(string type, params byte[][] parts)
    {
        var body = parts.SelectMany(part => part).ToArray();
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] LargeBox(string type, byte[] body)
    {
        var box = new byte[16 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        body.CopyTo(box, 16);
        return box;
    }
}
