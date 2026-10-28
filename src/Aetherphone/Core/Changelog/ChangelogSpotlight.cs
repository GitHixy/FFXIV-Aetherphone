using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Changelog;

internal readonly struct ChangelogSpotlight
{
    public readonly string AppId;
    public readonly LocString Title;
    public readonly LocString Body;

    public ChangelogSpotlight(string appId, LocString title, LocString body)
    {
        AppId = appId;
        Title = title;
        Body = body;
    }
}
