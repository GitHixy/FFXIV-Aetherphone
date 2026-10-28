namespace Aetherphone.Core.Aethernet;

internal static class ServerBackedApps
{
    private static readonly string[] AppIds =
    {
        "message", "chirper", "aethergram", "velvet", "muster", "yellowpages", "announcements", "polls",
        "casino", "coin", "prism", "loveisland", "feedback",
    };

    public static bool Contains(string appId)
    {
        for (var index = 0; index < AppIds.Length; index++)
        {
            if (string.Equals(appId, AppIds[index], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
