using Dalamud.Game.ClientState.Keys;

namespace Aetherphone.Core.Platform;

internal static class GameKeys
{
    public static bool Down(VirtualKey key)
    {
        var keyState = Plugin.KeyState;
        return keyState.IsVirtualKeyValid(key) && keyState[key];
    }

    public static void Swallow(VirtualKey key)
    {
        var keyState = Plugin.KeyState;
        if (keyState.IsVirtualKeyValid(key))
        {
            keyState[key] = false;
        }
    }
}
