using Aetherphone.Core.Photos;
using Xunit;

namespace Aetherphone.Tests;

public sealed class PhotoLibraryWatchTests : IDisposable
{
    private readonly DirectoryInfo root;
    private readonly PhotoLibrary library;

    public PhotoLibraryWatchTests()
    {
        root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "aetherphone-photowatch-" + Guid.NewGuid().ToString("N")));
        root.Create();
        library = new PhotoLibrary(root);
    }

    public void Dispose()
    {
        if (root.Exists)
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void NothingChangedRightAfterMarking()
    {
        var watch = new PhotoLibraryWatch();
        watch.Mark(library);

        Assert.False(watch.Changed(library));
    }

    [Fact]
    public void ALibraryChangeIsReportedOnce()
    {
        var watch = new PhotoLibraryWatch();
        watch.Mark(library);
        library.MarkChanged();

        Assert.True(watch.Changed(library));
        Assert.False(watch.Changed(library));
    }

    [Fact]
    public void AChangeBeforeMarkingIsNotReported()
    {
        var watch = new PhotoLibraryWatch();
        library.MarkChanged();
        watch.Mark(library);

        Assert.False(watch.Changed(library));
    }
}
