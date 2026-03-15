using AzureWebLogDownloader.Configuration;
using AzureWebLogDownloader.Services;
using Microsoft.Extensions.Configuration;

const string ProjectName = "azure-web-log-downloader";

var cliOptions = ParseCliOptions(args);
var configuration = BuildConfiguration(ProjectName, cliOptions.ConfigPath);

var webLogOptions = new WebLogOptions();
configuration.GetSection("Azure:WebLogs").Bind(webLogOptions);

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

using var logger = OperationalLogger.Create(webLogOptions);
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
var matchedCandidates = new List<BlobPathMatch>();
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
    }
    catch (Exception ex)
    {
        sourceFailures++;
        logger.Warn(
            "source.scan.failed",
            $"container={source.ContainerUrl} prefix={source.PathPrefix} error=\"{ex.Message}\"");
        continue;
    }

    var sampleBlobPath = BuildSampleBlobPath(source.PathPrefix, dateRange.EndDateUtc, suffix: $"sample-{index}.log");
    if (templateResolver.TryResolve(
        sampleBlobPath,
        webLogOptions.BlobPathTemplates,
        out var sampleMatch,
        traceLogger: message => logger.Info("template.match", message)))
    {
        matchedCandidates.Add(sampleMatch!);
    }
    else
    {
        logger.Warn(
            "template.unmatched",
            $"blobPath={sampleBlobPath} templateCount={webLogOptions.BlobPathTemplates.Count}");
    }

    if (index == 0)
    {
        var conflictBlobPath = BuildSampleBlobPath(source.PathPrefix, dateRange.EndDateUtc, suffix: "sample-conflict.log");
        if (templateResolver.TryResolve(
            conflictBlobPath,
            webLogOptions.BlobPathTemplates,
            out var conflictMatch,
            traceLogger: message => logger.Info("template.match", message)))
        {
            matchedCandidates.Add(conflictMatch!);
        }
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

var resolved = templateResolver.ResolveDeterministicConflicts(matchedCandidates);
logger.Info(
    "download.resolve",
    $"matchedCandidates={matchedCandidates.Count} logicalKeys={resolved.Count} conflictPolicy=templateOrderThenPath");

var persistenceService = new WebLogDownloadService();
PersistenceResult persistence;
try
{
    persistence = await persistenceService.PersistAsync(webLogOptions.SaveAllBlobsDirectory!, resolved);
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
    $"outputRoot={persistence.OutputRootPath} written={persistence.WrittenCount} overwritten={persistence.OverwrittenCount}");
foreach (var persisted in persistence.Files)
{
    logger.Info("download.file", $"key={persisted.LogicalKey} path={persisted.FilePath}");
}

logger.Info(
    "run.end",
    $"durationMs={(long)(DateTime.UtcNow - runStartedAt).TotalMilliseconds} sourceFailures={sourceFailures} written={persistence.WrittenCount} overwritten={persistence.OverwrittenCount}");

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
        configurationBuilder.AddJsonFile(homeDirectoryConfigPath, optional: true, reloadOnChange: false);
    }

    // Project-local default config (e.g. ./.azure-web-log-downloader).
    configurationBuilder.AddJsonFile(currentDirectoryConfigPath, optional: true, reloadOnChange: false);

    // Explicit config path takes precedence over default files.
    if (!string.IsNullOrWhiteSpace(configPath))
    {
        var fullConfigPath = Path.GetFullPath(configPath);
        configurationBuilder.AddJsonFile(fullConfigPath, optional: false, reloadOnChange: false);
    }

    return configurationBuilder
        // Highest precedence for file-based settings.
        .AddEnvironmentVariables()
        .Build();
}

static CliOptions ParseCliOptions(string[] args)
{
    var mode = CliMode.Daily;
    string? configPath = null;
    DateTime? startDateUtc = null;
    DateTime? endDateUtc = null;

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
        endDateUtc);
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

static string BuildSampleBlobPath(string pathPrefix, DateTime dateUtc, string suffix = "sample.log")
{
    var prefix = string.IsNullOrWhiteSpace(pathPrefix)
        ? "SAMPLE-INSTANCE"
        : pathPrefix.TrimEnd('/');

    return $"{prefix}/{dateUtc:yyyy}/{dateUtc:MM}/{dateUtc:dd}/{dateUtc:HH}/{suffix}";
}

static void ExitWithCliError(string message)
{
    Console.Error.WriteLine($"CLI validation failed: {message}");
    Environment.Exit(1);
}

internal sealed record CliOptions(
    CliMode Mode,
    string? ConfigPath,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc);
