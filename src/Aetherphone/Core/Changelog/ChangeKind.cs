using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Changelog;

internal enum ChangeKind : byte
{
    New,
    Improved,
    Fixed,
}

internal static class ChangeKinds
{
    private const string AddedVerb = "Added";
    private const string FixedVerb = "Fixed";

    public static ChangeKind Of(in LocString bullet)
    {
        if (bullet.Source.StartsWith(AddedVerb, StringComparison.Ordinal))
        {
            return ChangeKind.New;
        }

        return bullet.Source.StartsWith(FixedVerb, StringComparison.Ordinal) ? ChangeKind.Fixed : ChangeKind.Improved;
    }
}
