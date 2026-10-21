namespace Aetherphone.Core.Campaigns;

internal static class CampaignShellBridge
{
    public const int NightMinute = 22 * 60;

    public static Func<string, string, bool>? UseIconOverride { get; set; }

    public static Action<string>? ClearIconOverride { get; set; }

    public static Action<string, int>? HoldNight { get; set; }

    public static Action<string>? ReleaseNight { get; set; }
}
