namespace Aetherphone.Core.Social;

internal enum ScheduleIssue : byte
{
    None,
    TooSoon,
    TooLate,
}

internal static class ScheduleRules
{
    public const int MinimumLeadMinutes = 5;
    public const int MaximumLeadDays = 30;
    public const int MinuteStep = 5;

    private const int MinutesPerDay = 24 * 60;
    private const int DefaultLeadMinutes = 60;

    public static ScheduleIssue Check(long publishAtUnix, long nowUnix)
    {
        if (publishAtUnix < nowUnix + MinimumLeadMinutes * 60L)
        {
            return ScheduleIssue.TooSoon;
        }

        return publishAtUnix > nowUnix + MaximumLeadDays * 86400L ? ScheduleIssue.TooLate : ScheduleIssue.None;
    }

    public static long ToUnix(int dayOffset, int minuteOfDay, DateTime localToday) =>
        new DateTimeOffset(localToday.AddDays(dayOffset).AddMinutes(minuteOfDay)).ToUnixTimeSeconds();

    public static (int DayOffset, int MinuteOfDay) FromUnix(long unixSeconds, DateTime localToday)
    {
        var local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().DateTime;
        var dayOffset = Math.Clamp((int)(local.Date - localToday).TotalDays, 0, MaximumLeadDays);
        return (dayOffset, local.Hour * 60 + local.Minute);
    }

    public static long Suggested(DateTime nowLocal)
    {
        var target = nowLocal.AddMinutes(DefaultLeadMinutes);
        var minuteOfDay = target.Hour * 60 + target.Minute;
        var rounded = (minuteOfDay + MinuteStep - 1) / MinuteStep * MinuteStep;
        var day = target.Date;
        if (rounded >= MinutesPerDay)
        {
            day = day.AddDays(1);
            rounded -= MinutesPerDay;
        }

        return new DateTimeOffset(day.AddMinutes(rounded)).ToUnixTimeSeconds();
    }
}
