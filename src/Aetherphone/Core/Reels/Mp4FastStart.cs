using System.Buffers.Binary;

namespace Aetherphone.Core.Reels;

internal static class Mp4FastStart
{
    private const int FullBoxHeader = 4;
    private const int EntryCountSize = 4;
    private const int CopyBufferSize = 1 << 16;

    internal static bool Rewrite(Stream source, Stream destination)
    {
        var boxes = Mp4Boxes.ReadChildren(source, 0, source.Length);
        var movieIndex = IndexOf(boxes, Mp4Boxes.Movie);
        var mediaIndex = IndexOf(boxes, Mp4Boxes.MediaData);
        if (movieIndex < 0 || mediaIndex < 0)
        {
            throw new InvalidDataException("The file has no moov or no mdat box.");
        }

        if (movieIndex < mediaIndex)
        {
            return false;
        }

        var movie = boxes[movieIndex];
        var insertAt = boxes[0].Type == Mp4Boxes.FileType ? boxes[0].End : 0L;
        var movieBytes = Mp4Boxes.ReadBox(source, movie);
        ShiftChunkOffsets(movieBytes, insertAt, movie.Offset, movie.Size);

        var buffer = new byte[CopyBufferSize];
        CopyRange(source, destination, 0, insertAt, buffer);
        destination.Write(movieBytes);
        CopyRange(source, destination, insertAt, movie.Offset - insertAt, buffer);
        CopyRange(source, destination, movie.End, source.Length - movie.End, buffer);
        return true;
    }

    internal static int ShiftChunkOffsets(Span<byte> movie, long movedStart, long movedEnd, long shift)
    {
        var header = BinaryPrimitives.ReadUInt32BigEndian(movie) == 1 ? 16 : 8;
        return ShiftWithin(movie, header, movie.Length, movedStart, movedEnd, shift);
    }

    private static int ShiftWithin(Span<byte> data, int start, int end, long movedStart, long movedEnd, long shift)
    {
        var patched = 0;
        var children = Mp4Boxes.ReadChildren(data, start, end);
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var bodyStart = (int)child.Offset + child.HeaderSize;
            var bodyEnd = (int)child.End;
            switch (child.Type)
            {
                case Mp4Boxes.Track:
                case Mp4Boxes.Media:
                case Mp4Boxes.MediaInformation:
                case Mp4Boxes.SampleTable:
                    patched += ShiftWithin(data, bodyStart, bodyEnd, movedStart, movedEnd, shift);
                    break;
                case Mp4Boxes.ChunkOffsets:
                    patched += ShiftTable(data.Slice(bodyStart, bodyEnd - bodyStart), 4, movedStart, movedEnd, shift);
                    break;
                case Mp4Boxes.ChunkOffsets64:
                    patched += ShiftTable(data.Slice(bodyStart, bodyEnd - bodyStart), 8, movedStart, movedEnd, shift);
                    break;
            }
        }

        return patched;
    }

    private static int ShiftTable(Span<byte> body, int entrySize, long movedStart, long movedEnd, long shift)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(body[FullBoxHeader..]);
        var entries = body[(FullBoxHeader + EntryCountSize)..];
        if ((long)count * entrySize > entries.Length)
        {
            throw new InvalidDataException($"A chunk offset table claims {count} entries but holds {entries.Length} bytes.");
        }

        var patched = 0;
        for (var entryIndex = 0; entryIndex < count; entryIndex++)
        {
            var slot = entries.Slice(entryIndex * entrySize, entrySize);
            var offset = entrySize == 4
                ? BinaryPrimitives.ReadUInt32BigEndian(slot)
                : (long)BinaryPrimitives.ReadUInt64BigEndian(slot);
            if (offset < movedStart || offset >= movedEnd)
            {
                continue;
            }

            var moved = offset + shift;
            if (entrySize == 4)
            {
                if (moved > uint.MaxValue)
                {
                    throw new InvalidDataException("A 32-bit chunk offset would overflow after moving moov.");
                }

                BinaryPrimitives.WriteUInt32BigEndian(slot, (uint)moved);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(slot, (ulong)moved);
            }

            patched++;
        }

        return patched;
    }

    private static int IndexOf(List<Mp4Box> boxes, uint type)
    {
        for (var index = 0; index < boxes.Count; index++)
        {
            if (boxes[index].Type == type)
            {
                return index;
            }
        }

        return -1;
    }

    private static void CopyRange(Stream source, Stream destination, long start, long length, byte[] buffer)
    {
        source.Position = start;
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                throw new EndOfStreamException("The file ended before the copy finished.");
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
