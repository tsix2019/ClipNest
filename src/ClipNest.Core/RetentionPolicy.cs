namespace ClipNest.Core;

/// <summary>How long unpinned history is kept. Values are persisted in settings — never renumber.</summary>
public enum RetentionPeriod
{
    Day = 0,
    Week = 1,
    Month = 2,
    Year = 3,
    Forever = 4,
}

public static class RetentionPolicy
{
    /// <summary>Maximum number of unpinned items kept regardless of the period.</summary>
    public const int HardCap = 50_000;

    /// <summary>Items created before the returned instant are expired; null means keep forever.</summary>
    public static DateTimeOffset? Cutoff(RetentionPeriod period, DateTimeOffset now) => period switch
    {
        RetentionPeriod.Day => now.AddDays(-1),
        RetentionPeriod.Week => now.AddDays(-7),
        RetentionPeriod.Month => now.AddMonths(-1),
        RetentionPeriod.Year => now.AddYears(-1),
        _ => null,
    };

    /// <summary>True when switching periods could delete existing items (the UI asks for confirmation).</summary>
    public static bool IsShrinking(RetentionPeriod from, RetentionPeriod to) => to < from;
}
