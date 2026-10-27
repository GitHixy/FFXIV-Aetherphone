using Aetherphone.Apps.Aethergram;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ExploreMosaicTests
{
    [Fact]
    public void EveryBlockOpensWithALargeTileThatSwitchesSides()
    {
        Assert.Equal(new MosaicCell(0, 0, 2), ExploreMosaic.Cell(0));
        Assert.Equal(new MosaicCell(2, 0, 1), ExploreMosaic.Cell(1));
        Assert.Equal(new MosaicCell(2, 1, 1), ExploreMosaic.Cell(2));
        Assert.Equal(new MosaicCell(0, 2, 1), ExploreMosaic.Cell(3));
        Assert.Equal(new MosaicCell(2, 3, 1), ExploreMosaic.Cell(8));
        Assert.Equal(new MosaicCell(1, 4, 2), ExploreMosaic.Cell(9));
        Assert.Equal(new MosaicCell(0, 4, 1), ExploreMosaic.Cell(10));
        Assert.Equal(new MosaicCell(0, 5, 1), ExploreMosaic.Cell(11));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(37)]
    public void NoTwoTilesShareACell(int count)
    {
        var rows = ExploreMosaic.Rows(count);
        var taken = new bool[rows * ExploreMosaic.Columns];
        for (var index = 0; index < count; index++)
        {
            var cell = ExploreMosaic.Cell(index);
            for (var row = cell.Row; row < cell.Row + cell.Span; row++)
            {
                for (var column = cell.Column; column < cell.Column + cell.Span; column++)
                {
                    Assert.InRange(column, 0, ExploreMosaic.Columns - 1);
                    var slot = row * ExploreMosaic.Columns + column;
                    Assert.False(taken[slot]);
                    taken[slot] = true;
                }
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(9, 4)]
    [InlineData(10, 6)]
    [InlineData(30, 14)]
    public void RowsCoverTheTallestTile(int count, int expectedRows)
    {
        Assert.Equal(expectedRows, ExploreMosaic.Rows(count));
    }
}
