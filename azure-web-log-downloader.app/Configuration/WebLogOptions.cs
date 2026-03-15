namespace AzureWebLogDownloader.Configuration;

public sealed class WebLogOptions
{
    public string? ConnectionString { get; init; }

    public List<string> BlobContainerUrls { get; init; } = [];

    public List<string> PathPrefixes { get; init; } = [];

    public List<string> BlobPathTemplates { get; init; } = [];

    public string? SaveAllBlobsDirectory { get; init; }

    public string FileNamePattern { get; init; } = "{instance}-{yyyyMMdd_HHmm}.log";

    public int DefaultDailyLookbackDays { get; init; } = 1;

    public int DefaultWeeklyLookbackDays { get; init; } = 7;

    public bool EnableFileLogging { get; init; }

    public string? FileLogPath { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ConnectionString) && BlobContainerUrls.Count == 0)
        {
            errors.Add("At least one Azure source is required: set Azure:WebLogs:ConnectionString or Azure:WebLogs:BlobContainerUrls.");
        }

        if (PathPrefixes.Count == 0)
        {
            errors.Add("Azure:WebLogs:PathPrefixes must include at least one blob path prefix.");
        }

        if (BlobPathTemplates.Count == 0)
        {
            errors.Add("Azure:WebLogs:BlobPathTemplates must include at least one ordered path template.");
        }

        if (string.IsNullOrWhiteSpace(SaveAllBlobsDirectory))
        {
            errors.Add("Azure:WebLogs:SaveAllBlobsDirectory is required.");
        }

        if (string.IsNullOrWhiteSpace(FileNamePattern))
        {
            errors.Add("Azure:WebLogs:FileNamePattern is required.");
        }

        if (DefaultDailyLookbackDays <= 0)
        {
            errors.Add("Azure:WebLogs:DefaultDailyLookbackDays must be greater than 0.");
        }

        if (DefaultWeeklyLookbackDays <= 0)
        {
            errors.Add("Azure:WebLogs:DefaultWeeklyLookbackDays must be greater than 0.");
        }

        return errors;
    }
}
