using Aetherphone.Core.Apps;
using Dalamud.Interface.Textures.TextureWraps;

namespace Aetherphone.Core.Social;

internal static class ChirpChatBridge
{
    public const string ChirperAppId = "chirper";

    private static ChirpCardStore? store;
    private static SocialLauncher? launcher;
    private static INavigator? navigation;

    public static void Bind(ChirpCardStore cardStore, SocialLauncher socialLauncher, INavigator navigator)
    {
        store = cardStore;
        launcher = socialLauncher;
        navigation = navigator;
    }

    public static void Clear()
    {
        store = null;
        launcher = null;
        navigation = null;
    }

    public static ChirpCardResolution Resolve(string postId)
    {
        return store?.Resolve(postId) ?? new ChirpCardResolution(null, Missed: false);
    }

    public static IDalamudTextureWrap? Thumbnail(string url) => store?.Thumbnail(url);

    public static void Open(string postId)
    {
        if (launcher is null || navigation is null || !navigation.IsAvailable(ChirperAppId))
        {
            return;
        }

        launcher.Request(ChirperAppId, new SocialDeepLink(SocialLinkKind.Post, postId));
        navigation.Open(ChirperAppId);
    }
}
