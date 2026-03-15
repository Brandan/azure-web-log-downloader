namespace AzureWebLogDownloader.Services;

public sealed class FileNameBuilder
{
    public string BuildFileName(string instance, DateTime timestampUtc)
    {
        var sanitizedInstance = SanitizeSegment(instance);
        return $"{sanitizedInstance}-{timestampUtc:yyyyMMdd_HHmm}.log";
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
