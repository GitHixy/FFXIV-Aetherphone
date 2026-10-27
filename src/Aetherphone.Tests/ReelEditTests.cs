using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelEditTests
{
    [Fact]
    public void ALongClipStartsWithTheFirstNinetySeconds()
    {
        var edit = ReelEdit.Begin(240d, 1920, 1080);

        Assert.Equal(0d, edit.StartSeconds);
        Assert.Equal(ReelFormat.MaxDurationSeconds, edit.EndSeconds);
        Assert.Equal(0d, edit.CoverSeconds);
        Assert.Equal(ReelFraming.Original, edit.Framing);
        Assert.Null(edit.Crop);
    }

    [Fact]
    public void AShortClipKeepsAllOfIt()
    {
        var edit = ReelEdit.Begin(12.5d, 720, 1280);

        Assert.Equal(12.5d, edit.EndSeconds);
        Assert.Equal(12.5d, edit.LengthSeconds);
    }

    [Fact]
    public void DraggingTheEndPastNinetySecondsPullsTheStartAlong()
    {
        var edit = ReelEdit.Begin(240d, 1920, 1080);

        edit.MoveEnd(200d);
        Assert.Equal(200d, edit.EndSeconds);
        Assert.Equal(200d - ReelFormat.MaxDurationSeconds, edit.StartSeconds);

        edit.MoveEnd(1000d);
        Assert.Equal(240d, edit.EndSeconds);
        Assert.Equal(ReelFormat.MaxDurationSeconds, edit.LengthSeconds);
    }

    [Fact]
    public void DraggingTheStartBackPastNinetySecondsPullsTheEndAlong()
    {
        var edit = ReelEdit.Begin(240d, 1920, 1080);
        edit.MoveEnd(200d);

        edit.MoveStart(0d);
        Assert.Equal(0d, edit.StartSeconds);
        Assert.Equal(ReelFormat.MaxDurationSeconds, edit.EndSeconds);
    }

    [Fact]
    public void TheHandlesNeverCrossAndKeepASecondBetweenThem()
    {
        var edit = ReelEdit.Begin(240d, 1920, 1080);
        edit.MoveStart(30d);

        edit.MoveStart(500d);
        Assert.Equal(ReelFormat.MaxDurationSeconds - ReelEdit.MinimumLengthSeconds, edit.StartSeconds);

        edit.MoveEnd(0d);
        Assert.Equal(edit.StartSeconds + ReelEdit.MinimumLengthSeconds, edit.EndSeconds);
    }

    [Fact]
    public void SlidingKeepsTheLengthAndStopsAtTheEnds()
    {
        var edit = ReelEdit.Begin(100d, 1920, 1080);
        edit.MoveEnd(40d);

        edit.Slide(80d);
        Assert.Equal(60d, edit.StartSeconds);
        Assert.Equal(100d, edit.EndSeconds);

        edit.Slide(-5d);
        Assert.Equal(0d, edit.StartSeconds);
        Assert.Equal(40d, edit.EndSeconds);
    }

    [Fact]
    public void TheCoverStaysInsideTheKeptRange()
    {
        var edit = ReelEdit.Begin(100d, 1920, 1080);
        edit.SetCover(50d);
        Assert.Equal(50d, edit.CoverSeconds);

        edit.MoveEnd(30d);
        Assert.Equal(30d, edit.CoverSeconds);

        edit.MoveStart(10d);
        edit.SetCover(2d);
        Assert.Equal(10d, edit.CoverSeconds);

        edit.Slide(40d);
        Assert.Equal(40d, edit.CoverSeconds);
    }

    [Theory]
    [InlineData(1920, 1080, 0.25f)]
    [InlineData(1080, 1080, 1f)]
    [InlineData(1080, 2340, 0.5f)]
    public void FillUsesTheTranscodersCropWindow(int width, int height, float position)
    {
        var edit = ReelEdit.Begin(30d, width, height);
        edit.SetFraming(ReelFraming.Fill);
        edit.SetFillPosition(position);

        Assert.Equal(ReelCrop.FillPortrait(width, height, position), edit.Crop);
        var layout = ReelLayout.Plan(width, height, edit.Crop);
        Assert.Equal(ReelFormat.TallestAspect, (float)layout.Width / layout.Height, 2);
    }

    [Fact]
    public void AClipThatIsAlreadyNineBySixteenHasNothingToFill()
    {
        var edit = ReelEdit.Begin(30d, 720, 1280);
        edit.SetFraming(ReelFraming.Fill);

        Assert.False(edit.CanFill);
        Assert.Equal(ReelFraming.Original, edit.Framing);
        Assert.Null(edit.Crop);
    }

    [Fact]
    public void DraggingTheWindowAcrossItsTravelMovesItFromEdgeToEdge()
    {
        var edit = ReelEdit.Begin(30d, 1920, 1080);
        edit.SetFraming(ReelFraming.Fill);
        edit.SetFillPosition(0f);
        var window = edit.Crop!.Value.Width;
        const float previewWidth = 400f;

        edit.DragFill((1f - window) * previewWidth * 0.5f, previewWidth);
        Assert.Equal(0.5f, edit.FillPosition, 3);

        edit.DragFill(previewWidth * 10f, previewWidth);
        Assert.Equal(1f, edit.FillPosition);
        Assert.Equal(1f - window, edit.Crop!.Value.X, 3);
    }

    [Fact]
    public void ATallClipDragsVertically()
    {
        var edit = ReelEdit.Begin(30d, 1080, 2340);
        edit.SetFraming(ReelFraming.Fill);
        edit.SetFillPosition(0f);

        Assert.False(edit.FillsHorizontally);
        var crop = edit.Crop!.Value;
        edit.DragFill((1f - crop.Height) * 500f, 500f);
        Assert.Equal(1f, edit.FillPosition, 3);
        Assert.Equal(1f - crop.Height, edit.Crop!.Value.Y, 3);
    }

    [Fact]
    public void DraggingDoesNothingInOriginalFraming()
    {
        var edit = ReelEdit.Begin(30d, 1920, 1080);

        edit.DragFill(100f, 400f);

        Assert.Equal(0.5f, edit.FillPosition);
    }
}
