using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Changelog;

internal readonly struct ChangelogEntry
{
    private const string TrailingRevision = ".0";
    private const int ReleaseDots = 3;

    public readonly string Version;
    public readonly string ShortVersion;
    public readonly string MinorLine;
    public readonly string Date;
    public readonly IReadOnlyList<LocString> Highlights;
    public readonly IReadOnlyList<ChangelogSection> Sections;
    public readonly IReadOnlyList<ChangelogSpotlight> Spotlight;
    public readonly ChangeTally Tally;

    public ChangelogEntry(string version, string date, IReadOnlyList<LocString> highlights)
        : this(version, date, highlights, Array.Empty<ChangelogSection>(), Array.Empty<ChangelogSpotlight>())
    {
    }

    public ChangelogEntry(string version, string date, IReadOnlyList<ChangelogSection> sections)
        : this(version, date, Array.Empty<LocString>(), sections, Array.Empty<ChangelogSpotlight>())
    {
    }

    public ChangelogEntry(string version, string date, IReadOnlyList<ChangelogSpotlight> spotlight,
        IReadOnlyList<ChangelogSection> sections)
        : this(version, date, Array.Empty<LocString>(), sections, spotlight)
    {
    }

    private ChangelogEntry(string version, string date, IReadOnlyList<LocString> highlights,
        IReadOnlyList<ChangelogSection> sections, IReadOnlyList<ChangelogSpotlight> spotlight)
    {
        Version = version;
        ShortVersion = Shorten(version);
        MinorLine = MinorOf(version);
        Date = date;
        Highlights = highlights;
        Sections = sections;
        Spotlight = spotlight;
        Tally = ChangeTally.From(highlights, sections);
    }

    private static string Shorten(string version)
    {
        var isRelease = version.AsSpan().Count('.') == ReleaseDots &&
                        version.EndsWith(TrailingRevision, StringComparison.Ordinal);
        return isRelease ? version[..^TrailingRevision.Length] : version;
    }

    private static string MinorOf(string version)
    {
        var firstDot = version.IndexOf('.');
        var secondDot = firstDot < 0 ? -1 : version.IndexOf('.', firstDot + 1);
        return secondDot < 0 ? version : version[..secondDot];
    }
}
