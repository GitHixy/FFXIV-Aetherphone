using System.Buffers.Binary;
using System.Text;

namespace Aetherphone.Core.Reels;

internal readonly record struct Mp4Box(uint Type, long Offset, long Size, int HeaderSize)
{
    internal long End => Offset + Size;
}

internal readonly record struct Mp4Layout(string BoxOrder, bool HasMovie, bool MoovBeforeMdat, double DurationSeconds);

internal static class Mp4Boxes
{
    internal const uint FileType = 'f' << 24 | 't' << 16 | 'y' << 8 | 'p';
    internal const uint Movie = 'm' << 24 | 'o' << 16 | 'o' << 8 | 'v';
    internal const uint MediaData = 'm' << 24 | 'd' << 16 | 'a' << 8 | 't';
    internal const uint MovieHeader = 'm' << 24 | 'v' << 16 | 'h' << 8 | 'd';
    internal const uint Track = 't' << 24 | 'r' << 16 | 'a' << 8 | 'k';
    internal const uint Media = 'm' << 24 | 'd' << 16 | 'i' << 8 | 'a';
    internal const uint MediaInformation = 'm' << 24 | 'i' << 16 | 'n' << 8 | 'f';
    internal const uint SampleTable = 's' << 24 | 't' << 16 | 'b' << 8 | 'l';
    internal const uint ChunkOffsets = 's' << 24 | 't' << 16 | 'c' << 8 | 'o';
    internal const uint ChunkOffsets64 = 'c' << 24 | 'o' << 16 | '6' << 8 | '4';

    private const int CompactHeader = 8;
    private const int LargeHeader = 16;

    internal static List<Mp4Box> ReadChildren(Stream stream, long start, long end)
    {
        var boxes = new List<Mp4Box>();
        Span<byte> header = stackalloc byte[LargeHeader];
        var position = start;
        while (position + CompactHeader <= end)
        {
            stream.Position = position;
            stream.ReadExactly(header[..CompactHeader]);
            var box = ParseHeader(header, position, end, stream);
            boxes.Add(box);
            position = box.End;
        }

        return boxes;
    }

    internal static List<Mp4Box> ReadChildren(ReadOnlySpan<byte> data, int start, int end)
    {
        var boxes = new List<Mp4Box>();
        var position = start;
        while (position + CompactHeader <= end)
        {
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            var type = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            var headerSize = CompactHeader;
            if (size == 1)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(position + CompactHeader)..]);
                headerSize = LargeHeader;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            CheckBounds(type, position, size, headerSize, end);
            boxes.Add(new Mp4Box(type, position, size, headerSize));
            position += (int)size;
        }

        return boxes;
    }

    internal static string TypeName(uint type)
    {
        Span<char> name = stackalloc char[4];
        name[0] = (char)(type >> 24 & 0xFF);
        name[1] = (char)(type >> 16 & 0xFF);
        name[2] = (char)(type >> 8 & 0xFF);
        name[3] = (char)(type & 0xFF);
        return new string(name);
    }

    internal static Mp4Layout Inspect(Stream stream)
    {
        var boxes = ReadChildren(stream, 0, stream.Length);
        var order = new StringBuilder();
        var movieIndex = -1;
        var mediaIndex = -1;
        for (var index = 0; index < boxes.Count; index++)
        {
            if (order.Length > 0)
            {
                order.Append(' ');
            }

            order.Append(TypeName(boxes[index].Type));
            if (boxes[index].Type == Movie && movieIndex < 0)
            {
                movieIndex = index;
            }
            else if (boxes[index].Type == MediaData && mediaIndex < 0)
            {
                mediaIndex = index;
            }
        }

        if (movieIndex < 0)
        {
            return new Mp4Layout(order.ToString(), false, false, 0d);
        }

        var movie = ReadBox(stream, boxes[movieIndex]);
        var moovBeforeMdat = mediaIndex < 0 || movieIndex < mediaIndex;
        return new Mp4Layout(order.ToString(), true, moovBeforeMdat, MovieDurationSeconds(movie));
    }

    internal static double MovieDurationSeconds(ReadOnlySpan<byte> movie)
    {
        var children = ReadChildren(movie, MovieHeaderStart(movie), movie.Length);
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (child.Type != MovieHeader)
            {
                continue;
            }

            var body = movie.Slice((int)child.Offset + child.HeaderSize, (int)child.Size - child.HeaderSize);
            var version = body[0];
            var timescale = version == 1
                ? BinaryPrimitives.ReadUInt32BigEndian(body[20..])
                : BinaryPrimitives.ReadUInt32BigEndian(body[12..]);
            var duration = version == 1
                ? BinaryPrimitives.ReadUInt64BigEndian(body[24..])
                : BinaryPrimitives.ReadUInt32BigEndian(body[16..]);
            return timescale == 0 ? 0d : (double)duration / timescale;
        }

        return 0d;
    }

    internal static byte[] ReadBox(Stream stream, in Mp4Box box)
    {
        var bytes = new byte[box.Size];
        stream.Position = box.Offset;
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static int MovieHeaderStart(ReadOnlySpan<byte> movie) =>
        BinaryPrimitives.ReadUInt32BigEndian(movie) == 1 ? LargeHeader : CompactHeader;

    private static Mp4Box ParseHeader(Span<byte> header, long position, long end, Stream stream)
    {
        var size = (long)BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        var headerSize = CompactHeader;
        if (size == 1)
        {
            stream.ReadExactly(header.Slice(CompactHeader, CompactHeader));
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[CompactHeader..]);
            headerSize = LargeHeader;
        }
        else if (size == 0)
        {
            size = end - position;
        }

        CheckBounds(type, position, size, headerSize, end);
        return new Mp4Box(type, position, size, headerSize);
    }

    private static void CheckBounds(uint type, long position, long size, int headerSize, long end)
    {
        if (size >= headerSize && position + size <= end)
        {
            return;
        }

        throw new InvalidDataException($"The {TypeName(type)} box at byte {position} claims {size} bytes and overruns its parent.");
    }
}
