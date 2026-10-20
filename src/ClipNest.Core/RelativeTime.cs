namespace ClipNest.Core;

/// <summary>A localizable relative time: <see cref="Key"/> is a resource key, <see cref="Value"/> its number argument.</summary>
public readonly record struct RelativeTimeValue(string Key, int Value);

public static class RelativeTime
{
    public static RelativeTimeValue Describe(DateTimeOffset time, DateTimeOffset now)
    {
        var age = now - time;
        if (age < TimeSpan.FromMinutes(1)) return new("Time_JustNow", 0);
        if (age < TimeSpan.FromHours(1)) return new("Time_Minutes", (int)age.TotalMinutes);

        var localTime = time.ToOffset(now.Offset);
        var days = (now.Date - localTime.Date).Days;
        if (days == 0) return new("Time_Hours", (int)age.TotalHours);
        if (days == 1) return new("Time_Yesterday", 0);
        if (days < 7) return new("Time_Days", days);
        return new("Time_Date", 0);
    }

    /// <summary>Time until the displayed text could change; used to schedule UI refreshes.</summary>
    public static TimeSpan RefreshInterval(DateTimeOffset time, DateTimeOffset now) =>
        now - time < TimeSpan.FromHours(1) ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(10);
}
