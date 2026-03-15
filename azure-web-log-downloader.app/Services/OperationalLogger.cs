using AzureWebLogDownloader.Configuration;

namespace AzureWebLogDownloader.Services;

public sealed class OperationalLogger : IDisposable
{
    private readonly string? _filePath;
    private readonly bool _verbose;
    private readonly object _fileLock = new();

    private OperationalLogger(string? filePath, bool verbose)
    {
        _filePath = filePath;
        _verbose = verbose;
    }

    public static OperationalLogger Create(WebLogOptions options, bool verbose = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.EnableFileLogging)
        {
            return new OperationalLogger(filePath: null, verbose);
        }

        var baseDirectory = string.IsNullOrWhiteSpace(options.SaveAllBlobsDirectory)
            ? Directory.GetCurrentDirectory()
            : options.SaveAllBlobsDirectory!;
        var defaultPath = Path.Combine(baseDirectory, "azure-web-log-downloader.log");
        var configuredPath = string.IsNullOrWhiteSpace(options.FileLogPath) ? defaultPath : options.FileLogPath!;
        var fullPath = Path.GetFullPath(configuredPath);

        var parentDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        return new OperationalLogger(fullPath, verbose);
    }

    public void Info(string eventName, string message) => Write("INFO", eventName, message);

    public void Warn(string eventName, string message) => Write("WARN", eventName, message);

    public void Error(string eventName, string message) => Write("ERROR", eventName, message);

    private void Write(string level, string eventName, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {eventName} {message}";

        if (level == "INFO")
        {
            if (_verbose)
            {
                Console.WriteLine(line);
            }
        }
        else
        {
            Console.Error.WriteLine(line);
        }

        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        lock (_fileLock)
        {
            File.AppendAllText(_filePath, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        // No unmanaged resources currently, but keep IDisposable for future writer upgrades.
    }
}
