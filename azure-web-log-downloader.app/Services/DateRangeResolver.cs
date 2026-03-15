using AzureWebLogDownloader.Configuration;

namespace AzureWebLogDownloader.Services;

public sealed class DateRangeResolver
{
    public DateRangeUtc Resolve(CliMode mode, DateTime? startDateUtc, DateTime? endDateUtc, WebLogOptions options, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolvedNowUtc = (nowUtc ?? DateTime.UtcNow).Date;

        if (startDateUtc is not null || endDateUtc is not null)
        {
            var explicitStart = (startDateUtc ?? endDateUtc ?? resolvedNowUtc).Date;
            var explicitEnd = (endDateUtc ?? startDateUtc ?? resolvedNowUtc).Date;
            if (explicitStart > explicitEnd)
            {
                throw new InvalidOperationException("Resolved start date is later than resolved end date.");
            }

            return new DateRangeUtc(explicitStart, explicitEnd, DateRangeSource.ExplicitInput);
        }

        return mode switch
        {
            CliMode.Daily => ResolveDailyDefault(options, resolvedNowUtc),
            CliMode.Weekly => ResolveWeeklyDefault(options, resolvedNowUtc),
            _ => throw new InvalidOperationException($"Unsupported mode '{mode}'.")
        };
    }

    private static DateRangeUtc ResolveDailyDefault(WebLogOptions options, DateTime nowDateUtc)
    {
        // "Yesterday" by default, with configurable lookback if needed.
        var lookbackDays = Math.Max(1, options.DefaultDailyLookbackDays);
        var end = nowDateUtc.AddDays(-1);
        var start = end.AddDays(-(lookbackDays - 1));
        return new DateRangeUtc(start, end, DateRangeSource.DefaultDaily);
    }

    private static DateRangeUtc ResolveWeeklyDefault(WebLogOptions options, DateTime nowDateUtc)
    {
        var lookbackDays = Math.Max(1, options.DefaultWeeklyLookbackDays);
        var end = nowDateUtc.AddDays(-1);
        var start = end.AddDays(-(lookbackDays - 1));
        return new DateRangeUtc(start, end, DateRangeSource.DefaultWeekly);
    }
}

public sealed record DateRangeUtc(
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    DateRangeSource Source);

public enum DateRangeSource
{
    ExplicitInput,
    DefaultDaily,
    DefaultWeekly
}

public enum CliMode
{
    Daily,
    Weekly
}
