namespace Aetherphone.Apps.Aethergram;

internal readonly record struct MosaicCell(int Column, int Row, int Span);

// Instagram's Explore rhythm on a three column grid: each block of nine tiles opens with one tile two
// columns wide and two rows tall, two single tiles stacked beside it, then two full rows, and the large
// tile switches sides from one block to the next.
internal static class ExploreMosaic
{
    public const int Columns = 3;
    public const int TilesPerBlock = 9;
    public const int RowsPerBlock = 4;
    public const int LargeSpan = 2;

    private const int SideTiles = 2;

    public static MosaicCell Cell(int index)
    {
        var block = index / TilesPerBlock;
        var slot = index % TilesPerBlock;
        var top = block * RowsPerBlock;
        var largeOnLeft = block % 2 == 0;
        if (slot == 0)
        {
            return new MosaicCell(largeOnLeft ? 0 : Columns - LargeSpan, top, LargeSpan);
        }

        if (slot <= SideTiles)
        {
            return new MosaicCell(largeOnLeft ? Columns - 1 : 0, top + slot - 1, 1);
        }

        var rest = slot - 1 - SideTiles;
        return new MosaicCell(rest % Columns, top + LargeSpan + rest / Columns, 1);
    }

    public static int Rows(int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var last = Cell(count - 1);
        var blockTop = (count - 1) / TilesPerBlock * RowsPerBlock;
        return Math.Max(last.Row + last.Span, blockTop + LargeSpan);
    }
}
