namespace AzureWebLogDownloader.Services;

public sealed class FileNameBuilder
{
    public string BuildFileName(string instance, DateTime timestampUtc, string? fileNamePattern = null)
    {
        var sanitizedInstance = SanitizeSegment(instance);
        var pattern = string.IsNullOrWhiteSpace(fileNamePattern)
            ? "{instance}-{yyyyMMdd_HHmm}.log"
            : fileNamePattern.Trim();

        var formatted = pattern
            .Replace("{instance}", sanitizedInstance, StringComparison.Ordinal)
            .Replace("{yyyy}", timestampUtc.ToString("yyyy"), StringComparison.Ordinal)
            .Replace("{MM}", timestampUtc.ToString("MM"), StringComparison.Ordinal)
            .Replace("{dd}", timestampUtc.ToString("dd"), StringComparison.Ordinal)
            .Replace("{HH}", timestampUtc.ToString("HH"), StringComparison.Ordinal)
            .Replace("{mm}", timestampUtc.ToString("mm"), StringComparison.Ordinal)
            .Replace("{yyyyMMdd}", timestampUtc.ToString("yyyyMMdd"), StringComparison.Ordinal)
            .Replace("{yyyyMMdd_HHmm}", timestampUtc.ToString("yyyyMMdd_HHmm"), StringComparison.Ordinal);

        var invalidChars = Path.GetInvalidFileNameChars();
        var safeFileName = new string(formatted
            .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(safeFileName)
            ? $"{sanitizedInstance}-{timestampUtc:yyyyMMdd_HHmm}.log"
            : safeFileName;
    }

    public string BuildLogicalKey(string instance, DateTime timestampUtc)
    {
        var sanitizedInstance = SanitizeSegment(instance);
        return $"{sanitizedInstance}|{timestampUtc:yyyyMMdd_HHmm}";
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown-instance";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown-instance" : sanitized;
    }
}
