namespace QueenZone.Data;

/// <summary>
/// Settings for <see cref="SlowQueryLoggingInterceptor"/>. Bound from <c>SqlPerformance</c>.
/// </summary>
public sealed class SlowQueryLoggingOptions
{
    public const string SectionName = "SqlPerformance";

    /// <summary>Commands at or above this duration are logged at Warning. 0 disables logging.</summary>
    public int SlowQueryThresholdMilliseconds { get; set; } = 500;
}
