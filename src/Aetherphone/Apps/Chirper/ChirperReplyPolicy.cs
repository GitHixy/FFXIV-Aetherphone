using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Interface;

namespace Aetherphone.Apps.Chirper;

internal static class ChirperReplyPolicy
{
    public const int Everyone = 0;

    public const int Following = 1;

    public const int Mentioned = 2;

    public const int Count = 3;

    public static LocString Option(int policy) => policy switch
    {
        Following => L.Social.AudienceFollowing,
        Mentioned => L.Chirper.ReplyOptionMentioned,
        _ => L.Social.AudienceEveryone,
    };

    public static LocString Summary(int policy) => policy switch
    {
        Following => L.Chirper.ReplyFollowingCan,
        Mentioned => L.Chirper.ReplyMentionedCan,
        _ => L.Chirper.ReplyEveryoneCan,
    };

    public static LocString Limit(int policy) =>
        policy == Following ? L.Chirper.ReplyLimitedFollowing : L.Chirper.ReplyLimitedMentioned;

    public static string Glyph(int policy) => policy switch
    {
        Following => PhoneIcons.UserCheck,
        Mentioned => IconGlyph.Of(FontAwesomeIcon.At),
        _ => PhoneIcons.World,
    };
}
