using System.Collections.Frozen;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Dalamud.Interface;

namespace Aetherphone.Core.Changelog;

internal readonly struct ChangelogTopic
{
    public readonly string? AppId;
    public readonly FontAwesomeIcon Icon;
    public readonly Vector4 Tint;

    public ChangelogTopic(string? appId, FontAwesomeIcon icon, Vector4 tint)
    {
        AppId = appId;
        Icon = icon;
        Tint = tint;
    }

    public static ChangelogTopic App(string appId) => new(appId, default, AccentRing.Fallback);
}

internal static class ChangelogTopics
{
    private const string ActivityAppId = "character";
    private const string HealthAppId = "health";
    public static readonly ChangelogTopic General = new(null, FontAwesomeIcon.Star, AccentRing.Violet);
    private static readonly FrozenDictionary<string, ChangelogTopic> ByTitleKey = Build();

    public static ChangelogTopic For(in LocString title) =>
        ByTitleKey.TryGetValue(title.Key, out var topic) ? topic : General;

    private static FrozenDictionary<string, ChangelogTopic> Build()
    {
        var topics = new Dictionary<string, ChangelogTopic>(StringComparer.Ordinal)
        {
            [L.Changelog.SectionPhone.Key] = new(null, FontAwesomeIcon.MobileAlt, AccentRing.Slate),
            [L.Changelog.SectionMessaging.Key] = new(null, FontAwesomeIcon.Comments, AccentRing.Green),
            [L.Changelog.SectionSocial.Key] = new(null, FontAwesomeIcon.Users, AccentRing.Azure),
            [L.Changelog.SectionAethergramVelvet.Key] = new(null, FontAwesomeIcon.Users, AccentRing.Orchid),
            [L.Changelog.SectionChirperAethergramVelvet.Key] = new(null, FontAwesomeIcon.Users, AccentRing.Indigo),
            [L.Changelog.SectionWidgets.Key] = new(null, FontAwesomeIcon.ThLarge, AccentRing.Indigo),
            [L.Changelog.SectionTours.Key] = new(null, FontAwesomeIcon.Route, AccentRing.Teal),
            [L.Changelog.SectionSounds.Key] = new(null, FontAwesomeIcon.VolumeUp, AccentRing.Rose),
            [L.Changelog.SectionHalloween.Key] = new(null, FontAwesomeIcon.Ghost, AccentRing.Orange),
            [L.Changelog.SectionGuidelines.Key] = new(null, FontAwesomeIcon.ShieldAlt, AccentRing.Slate),
            [L.Settings.LiquidGlass.Key] = new(null, FontAwesomeIcon.Magic, AccentRing.Cyan),
            [L.Phone.EnableTitle.Key] = new(null, FontAwesomeIcon.Phone, AccentRing.Green),
            [L.Character.Activity.Key] = ChangelogTopic.App(ActivityAppId),
            [L.Health.Title.Key] = ChangelogTopic.App(HealthAppId),
        };
        var appIds = AppStoreCatalog.AppIds;
        for (var index = 0; index < appIds.Length; index++)
        {
            topics.TryAdd(AppStoreCatalog.For(appIds[index]).Name.Key, ChangelogTopic.App(appIds[index]));
        }

        return topics.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
