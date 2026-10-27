using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;

namespace Aetherphone.Windows.Components;

internal static class SocialMute
{
    public static string MenuLabel(bool muted, string displayName, string handle) =>
        Loc.T(muted ? L.Social.UnmuteNamed : L.Social.MuteNamed, Target(displayName, handle));

    public static string PillLabel(bool muted) => Loc.T(muted ? L.Social.UnmuteAction : L.Social.MuteAction);

    public static void Apply(SocialFeedStore store, ScreenToast toast, string userId, bool mute, string displayName,
        string handle)
    {
        toast.Show(Loc.T(mute ? L.Social.MutedToast : L.Social.UnmutedToast, Target(displayName, handle)));
        store.SetMuted(userId, mute, saved =>
        {
            if (!saved)
            {
                toast.Show(Loc.T(L.Social.MuteFailed));
            }
        });
    }

    private static string Target(string displayName, string handle) =>
        handle.Length > 0 ? "@" + handle : displayName;
}
