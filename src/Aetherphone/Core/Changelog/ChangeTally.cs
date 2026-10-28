using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Changelog;

internal readonly struct ChangeTally
{
    public readonly int New;
    public readonly int Improved;
    public readonly int Fixed;

    private ChangeTally(int newCount, int improvedCount, int fixedCount)
    {
        New = newCount;
        Improved = improvedCount;
        Fixed = fixedCount;
    }

    public int Total => New + Improved + Fixed;

    public int KindCount => (New > 0 ? 1 : 0) + (Improved > 0 ? 1 : 0) + (Fixed > 0 ? 1 : 0);

    public int For(ChangeKind kind) => kind switch
    {
        ChangeKind.New => New,
        ChangeKind.Fixed => Fixed,
        _ => Improved,
    };

    public static ChangeTally From(IReadOnlyList<LocString> highlights, IReadOnlyList<ChangelogSection> sections)
    {
        Span<int> counts = stackalloc int[3];
        Accumulate(highlights, counts);
        for (var index = 0; index < sections.Count; index++)
        {
            Accumulate(sections[index].Highlights, counts);
        }

        return new ChangeTally(counts[(int)ChangeKind.New], counts[(int)ChangeKind.Improved],
            counts[(int)ChangeKind.Fixed]);
    }

    private static void Accumulate(IReadOnlyList<LocString> bullets, Span<int> counts)
    {
        for (var index = 0; index < bullets.Count; index++)
        {
            counts[(int)ChangeKinds.Of(bullets[index])]++;
        }
    }
}
