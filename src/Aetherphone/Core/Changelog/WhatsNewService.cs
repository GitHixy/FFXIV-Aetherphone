namespace Aetherphone.Core.Changelog;

internal sealed class WhatsNewService
{
    public const int MaxItems = 5;
    private readonly Configuration configuration;
    private readonly ChangelogSpotlight[] items = new ChangelogSpotlight[MaxItems];
    private bool collected;
    private int count;

    public WhatsNewService(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public bool Active { get; private set; }

    public ReadOnlySpan<ChangelogSpotlight> Items => items.AsSpan(0, count);

    public bool Due => !Active && configuration.HasUnseenChangelog && CollectOnce() > 0;

    public void Present() => Active = true;

    public void Dismiss()
    {
        Active = false;
        configuration.MarkChangelogSeen();
    }

    private int CollectOnce()
    {
        if (collected)
        {
            return count;
        }

        collected = true;
        var lastSeen = configuration.LastSeenChangelogVersion;
        var entries = ChangelogData.Entries;
        for (var entryIndex = 0; entryIndex < entries.Count && count < MaxItems; entryIndex++)
        {
            var entry = entries[entryIndex];
            if (entry.Version == lastSeen)
            {
                break;
            }

            var spotlight = entry.Spotlight;
            for (var spotIndex = 0; spotIndex < spotlight.Count && count < MaxItems; spotIndex++)
            {
                items[count++] = spotlight[spotIndex];
            }
        }

        return count;
    }
}
