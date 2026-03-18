using AzureWebLogDownloader.Configuration;
using AzureWebLogDownloader.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

const string ProjectName = "azure-web-log-downloader";

var cliOptions = ParseCliOptions(args);
if (cliOptions.ShowHelp)
{
    PrintHelp();
    return;
}

var configuration = BuildConfiguration(ProjectName, cliOptions.ConfigPath);
var configSourceDescriptions = DescribeConfigurationSources(ProjectName, cliOptions.ConfigPath);
if (cliOptions.Verbose)
{
    Console.WriteLine(
        $"{DateTime.UtcNow:O} [INFO] config.sources {string.Join(" | ", configSourceDescriptions)}");
}

var webLogOptions = BindWebLogOptions(configuration);

var validationErrors = webLogOptions.Validate();
if (validationErrors.Count > 0)
{
    Console.Error.WriteLine("Configuration validation failed:");
    foreach (var error in validationErrors)
    {
        Console.Error.WriteLine($"- {error}");
    }

    Environment.ExitCode = 1;
    return;
}

using var logger = OperationalLogger.Create(webLogOptions, cliOptions.Verbose);
var runStartedAt = DateTime.UtcNow;
logger.Info(
    "run.start",
    $"app=azure-web-log-downloader mode={cliOptions.Mode} fileLogging={webLogOptions.EnableFileLogging} outputRoot={webLogOptions.SaveAllBlobsDirectory}");
if (cliOptions.StartDateUtc is not null || cliOptions.EndDateUtc is not null)
{
    logger.Info(
        "run.input.override",
        $"start={cliOptions.StartDateUtc:yyyy-MM-dd} end={cliOptions.EndDateUtc:yyyy-MM-dd}");
}

var dateRangeResolver = new DateRangeResolver();
var dateRange = dateRangeResolver.Resolve(
    cliOptions.Mode,
    cliOptions.StartDateUtc,
    cliOptions.EndDateUtc,
    webLogOptions);
logger.Info(
    "run.range.resolved",
    $"source={dateRange.Source} start={dateRange.StartDateUtc:yyyy-MM-dd} end={dateRange.EndDateUtc:yyyy-MM-dd}");

var blobClientFactory = new AzureBlobClientFactory();
var sourceTargets = blobClientFactory.BuildSourceTargets(webLogOptions);
logger.Info("source.targets.resolved", $"count={sourceTargets.Count}");
if (sourceTargets.Count == 0)
{
    logger.Warn("source.targets.empty", "No source targets resolved from BlobContainerUrls and PathPrefixes.");
    logger.Info("run.end", $"durationMs={(long)(DateTime.UtcNow - runStartedAt).TotalMilliseconds} status=no-op");
    return;
}

var templateResolver = new BlobPathTemplateResolver();
var matchedCandidates = new List<BlobDownloadCandidate>();
var sourceFailures = 0;
for (var index = 0; index < sourceTargets.Count; index++)
{
    var source = sourceTargets[index];
    logger.Info(
        "source.scan.start",
        $"index={index + 1}/{sourceTargets.Count} container={source.ContainerUrl} prefix={source.PathPrefix} auth={source.AuthenticationMode}");

    try
    {
        await blobClientFactory.VerifyContainerAccessAsync(source.ContainerClient);
        logger.Info("source.scan.ok", $"container={source.ContainerUrl} prefix={source.PathPrefix}");
        var scannedCount = 0;
        var matchedInRangeCount = 0;
        var sampleBlobNames = new List<string>();
        await foreach (var blobItem in source.ContainerClient.GetBlobsAsync(prefix: source.PathPrefix))
        {
            scannedCount++;
            if (sampleBlobNames.Count < 3)
            {
                sampleBlobNames.Add(blobItem.Name);
            }
            if (!templateResolver.TryResolve(
                    blobItem.Name,
                    webLogOptions.BlobPathTemplates,
                    out var blobMatch,
                    traceLogger: message => logger.Info("template.match", message)))
            {
                continue;
            }

            var blobDateUtc = blobMatch!.TimestampUtc.Date;
            if (blobDateUtc < dateRange.StartDateUtc || blobDateUtc > dateRange.EndDateUtc)
            {
                continue;
            }

            matchedInRangeCount++;
            matchedCandidates.Add(new BlobDownloadCandidate(source.ContainerClient, blobMatch, blobItem.Properties.ContentLength));
        }

        logger.Info(
            "source.scan.complete",
            $"container={source.ContainerUrl} prefix={source.PathPrefix} scanned={scannedCount} matchedInRange={matchedInRangeCount} start={dateRange.StartDateUtc:yyyy-MM-dd} end={dateRange.EndDateUtc:yyyy-MM-dd} sampleBlobs=\"{string.Join(" ; ", sampleBlobNames)}\"");
    }
    catch (Exception ex)
    {
        sourceFailures++;
        logger.Warn(
            "source.scan.failed",
            $"container={source.ContainerUrl} prefix={source.PathPrefix} error=\"{ex.Message}\"");
        continue;
    }
}

if (matchedCandidates.Count == 0)
{
    logger.Warn("download.none", "No candidate blobs resolved from configured templates.");
    logger.Info(
        "run.end",
        $"durationMs={(long)(DateTime.UtcNow - runStartedAt).TotalMilliseconds} sourceFailures={sourceFailures} persisted=0");
    return;
}

var resolvedMatches = templateResolver.ResolveDeterministicConflicts(matchedCandidates.Select(candidate => candidate.Match).ToList());
var candidateIndex = matchedCandidates
    .GroupBy(candidate => BuildCandidateKey(candidate.Match), StringComparer.Ordinal)
    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
var resolved = resolvedMatches
    .Select(match => candidateIndex[BuildCandidateKey(match)])
    .ToList();
logger.Info(
    "download.resolve",
    $"matchedCandidates={matchedCandidates.Count} logicalKeys={resolved.Count} conflictPolicy=templateOrderThenPath");

var persistenceService = new WebLogDownloadService();
var progressReporter = cliOptions.ShowProgress ? new ConsoleProgressReporter() : null;
PersistenceResult persistence;
try
{
    persistence = await persistenceService.PersistAsync(
        webLogOptions.SaveAllBlobsDirectory!,
        resolved,
        webLogOptions.FileNamePattern,
        progressReporter is null ? null : progressReporter.Report);
}
catch (Exception ex)
{
    logger.Error("download.persist.failed", ex.Message);
    logger.Info(
        "run.end",
        $"durationMs={(long)(DateTime.UtcNow - runStartedAt).TotalMilliseconds} sourceFailures={sourceFailures} status=failed");
    Environment.ExitCode = 1;
    return;
}

logger.Info(
    "download.persist.complete",
    $"outputRoot={persistence.OutputRootPath} written={persistence.WrittenCount} skipped={persistence.SkippedCount} overwritten={persistence.OverwrittenCount}");
foreach (var persisted in persistence.Files)
{
    logger.Info(
        "download.file",
        $"key={persisted.LogicalKey} status={persisted.Disposition} remoteSize={FormatBytes(persisted.RemoteSizeBytes)} localSize={FormatBytes(persisted.LocalSizeBytes)} path={persisted.FilePath}");
}

logger.Info(
    "run.end",
    $"durationMs={(long)(DateTime.UtcNow - runStartedAt).TotalMilliseconds} sourceFailures={sourceFailures} written={persistence.WrittenCount} skipped={persistence.SkippedCount} overwritten={persistence.OverwrittenCount}");

return;

static IConfigurationRoot BuildConfiguration(string projectName, string? configPath)
{
    var baseDirectory = Directory.GetCurrentDirectory();
    var executableDirectory = AppContext.BaseDirectory;
    var currentDirectoryConfigPath = Path.Combine(baseDirectory, $".{projectName}");
    var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var homeDirectoryConfigPath = string.IsNullOrWhiteSpace(homeDirectory)
        ? null
        : Path.Combine(homeDirectory, $".{projectName}");
    var configurationBuilder = new ConfigurationBuilder()
        .SetBasePath(baseDirectory)
        // Lowest precedence: scaffold defaults in working directory.
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        // Also load defaults bundled next to the app binaries.
        .AddJsonFile(Path.Combine(executableDirectory, "appsettings.json"), optional: true, reloadOnChange: false);

    // Global default config in the user's home directory.
    if (!string.IsNullOrWhiteSpace(homeDirectoryConfigPath))
    {
        AddJsonFileByAbsolutePath(configurationBuilder, homeDirectoryConfigPath, optional: true);
    }

    // Project-local default config (e.g. ./.azure-web-log-downloader).
    AddJsonFileByAbsolutePath(configurationBuilder, currentDirectoryConfigPath, optional: true);

    // Explicit config path takes precedence over default files.
    if (!string.IsNullOrWhiteSpace(configPath))
    {
        var fullConfigPath = Path.GetFullPath(configPath);
        AddJsonFileByAbsolutePath(configurationBuilder, fullConfigPath, optional: false);
    }

    return configurationBuilder
        // Highest precedence for file-based settings.
        .AddEnvironmentVariables()
        .Build();
}

static void AddJsonFileByAbsolutePath(IConfigurationBuilder configurationBuilder, string fullPath, bool optional)
{
    var directory = Path.GetDirectoryName(fullPath);
    var fileName = Path.GetFileName(fullPath);
    if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
    {
        if (!optional)
        {
            throw new InvalidOperationException($"Invalid configuration path: '{fullPath}'.");
        }

        return;
    }

    var fileProvider = new PhysicalFileProvider(directory, ExclusionFilters.None);
    configurationBuilder.AddJsonFile(fileProvider, fileName, optional, reloadOnChange: false);
}

static IReadOnlyList<string> DescribeConfigurationSources(string projectName, string? configPath)
{
    var baseDirectory = Directory.GetCurrentDirectory();
    var executableDirectory = AppContext.BaseDirectory;
    var currentDirectoryConfigPath = Path.Combine(baseDirectory, $".{projectName}");
    var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var homeDirectoryConfigPath = string.IsNullOrWhiteSpace(homeDirectory)
        ? null
        : Path.Combine(homeDirectory, $".{projectName}");

    var sources = new List<string>
    {
        $"workingDir/appsettings.json(optional): {Path.Combine(baseDirectory, "appsettings.json")}",
        $"executableDir/appsettings.json(optional): {Path.Combine(executableDirectory, "appsettings.json")}",
        $"home default(optional): {homeDirectoryConfigPath ?? "<none>"}",
        $"workingDir default(optional): {currentDirectoryConfigPath}"
    };

    if (!string.IsNullOrWhiteSpace(configPath))
    {
        sources.Add($"explicit --config(required): {Path.GetFullPath(configPath)}");
    }

    sources.Add("environment variables");
    return sources;
}

static string BuildCandidateKey(BlobPathMatch match) =>
    $"{match.LogicalMinuteKey}|{match.TemplateOrder}|{match.BlobPath}";

static string FormatBytes(long? value)
{
    if (!value.HasValue)
    {
        return "unknown";
    }

    return $"{value.Value}B";
}

static CliOptions ParseCliOptions(string[] args)
{
    var mode = CliMode.Daily;
    string? configPath = null;
    DateTime? startDateUtc = null;
    DateTime? endDateUtc = null;
    var verbose = false;
    var showProgress = false;
    var showHelp = false;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--mode":
                mode = ParseMode(ReadValue(args, ref i, arg));
                break;

            case "--start":
                startDateUtc = ParseDate(ReadValue(args, ref i, arg), "--start");
                break;

            case "--end":
                endDateUtc = ParseDate(ReadValue(args, ref i, arg), "--end");
                break;

            case "--config":
                configPath = ReadValue(args, ref i, arg);
                break;

            case "--verbose":
                verbose = true;
                break;

            case "--progress":
                showProgress = true;
                break;

            case "--help":
            case "-h":
                showHelp = true;
                break;

            default:
                ExitWithCliError($"Unknown argument: {arg}");
                break;
        }
    }

    if (startDateUtc is not null && endDateUtc is not null && startDateUtc > endDateUtc)
    {
        ExitWithCliError("--start cannot be later than --end.");
    }

    return new CliOptions(
        mode,
        configPath,
        startDateUtc,
        endDateUtc,
        verbose,
        showProgress,
        showHelp);
}

static CliMode ParseMode(string mode)
{
    if (mode.Equals("daily", StringComparison.OrdinalIgnoreCase))
    {
        return CliMode.Daily;
    }

    if (mode.Equals("weekly", StringComparison.OrdinalIgnoreCase))
    {
        return CliMode.Weekly;
    }

    ExitWithCliError("Invalid --mode. Supported values are: daily, weekly.");
    return CliMode.Daily;
}

static DateTime ParseDate(string value, string argumentName)
{
    if (!DateTime.TryParse(value, out var parsedDate))
    {
        ExitWithCliError($"Invalid date for {argumentName}: '{value}'. Use a parseable format such as yyyy-MM-dd.");
    }

    return DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
}

static string ReadValue(string[] args, ref int index, string argumentName)
{
    var valueIndex = index + 1;
    if (valueIndex >= args.Length || args[valueIndex].StartsWith("--", StringComparison.Ordinal))
    {
        ExitWithCliError($"Missing value for {argumentName}.");
    }

    index = valueIndex;
    return args[valueIndex];
}

static void ExitWithCliError(string message)
{
    Console.Error.WriteLine($"CLI validation failed: {message}");
    Console.Error.WriteLine("Use --help to see available options.");
    Environment.Exit(1);
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        azure-web-log-downloader

        Usage:
          azure-web-log-downloader [options]

        Options:
          --mode <daily|weekly>   Download mode (default: daily)
          --start <yyyy-MM-dd>    Inclusive UTC start date
          --end <yyyy-MM-dd>      Inclusive UTC end date
          --config <path>         Explicit config file path
          --progress              Show live download progress
          --verbose               Show info/debug logs
          --help, -h              Show this help output

        Examples:
          azure-web-log-downloader --mode daily
          azure-web-log-downloader --mode weekly --config ~/.azure-web-log-downloader
          azure-web-log-downloader --start 2026-03-14 --end 2026-03-14 --progress
        """);
}

static WebLogOptions BindWebLogOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Azure:WebLogs");
    return new WebLogOptions
    {
        ConnectionString = section["ConnectionString"],
        BlobContainerUrls = ReadList(section, "BlobContainerUrls"),
        PathPrefixes = ReadList(section, "PathPrefixes"),
        BlobPathTemplates = ReadList(section, "BlobPathTemplates"),
        SaveAllBlobsDirectory = section["SaveAllBlobsDirectory"],
        FileNamePattern = ReadStringOrDefault(section, "FileNamePattern", "{instance}-{yyyyMMdd_HHmm}.log"),
        DefaultDailyLookbackDays = ReadIntOrDefault(section, "DefaultDailyLookbackDays", 1),
        DefaultWeeklyLookbackDays = ReadIntOrDefault(section, "DefaultWeeklyLookbackDays", 7),
        EnableFileLogging = ReadBoolOrDefault(section, "EnableFileLogging", false),
        FileLogPath = section["FileLogPath"]
    };
}

static List<string> ReadList(IConfigurationSection section, string key)
{
    return section
        .GetSection(key)
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .ToList();
}

static int ReadIntOrDefault(IConfigurationSection section, string key, int defaultValue)
{
    return int.TryParse(section[key], out var parsed) ? parsed : defaultValue;
}

static bool ReadBoolOrDefault(IConfigurationSection section, string key, bool defaultValue)
{
    return bool.TryParse(section[key], out var parsed) ? parsed : defaultValue;
}

static string ReadStringOrDefault(IConfigurationSection section, string key, string defaultValue)
{
    var value = section[key];
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}

internal sealed class ConsoleProgressReporter
{
    private readonly object _lock = new();
    private DateTime _lastRenderAt = DateTime.MinValue;
    private int _lastRenderedLength;

    public void Report(DownloadProgressUpdate update)
    {
        lock (_lock)
        {
            if (!update.IsFileCompleted && DateTime.UtcNow - _lastRenderAt < TimeSpan.FromMilliseconds(120))
            {
                return;
            }

            _lastRenderAt = DateTime.UtcNow;
            var totalBytes = update.TotalBytes.HasValue ? FormatBytes(update.TotalBytes.Value) : "?";
            var copiedBytes = FormatBytes(update.BytesCopied);
            var percent = update.TotalBytes is > 0
                ? $" {(update.BytesCopied * 100.0 / update.TotalBytes.Value):0.0}%"
                : string.Empty;
            var line = $"[progress] file {update.FileNumber}/{update.TotalFiles} {update.FileName} {copiedBytes}/{totalBytes}{percent}";
            if (line.Length < _lastRenderedLength)
            {
                line = line + new string(' ', _lastRenderedLength - line.Length);
            }

            _lastRenderedLength = line.Length;
            Console.Error.Write($"\r{line}");
            if (update.IsFileCompleted && update.FileNumber == update.TotalFiles)
            {
                Console.Error.WriteLine();
            }
        }
    }

    private static string FormatBytes(long value)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value;
        var suffixIndex = 0;
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.#}{suffixes[suffixIndex]}";
    }
}

internal sealed record CliOptions(
    CliMode Mode,
    string? ConfigPath,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc,
    bool Verbose,
    bool ShowProgress,
    bool ShowHelp);
