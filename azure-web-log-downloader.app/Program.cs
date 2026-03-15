using AzureWebLogDownloader.Configuration;
using AzureWebLogDownloader.Services;
using Microsoft.Extensions.Configuration;

var cliOptions = ParseCliOptions(args);
var configuration = BuildConfiguration(cliOptions.ConfigPath);

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

var appName = "azure-web-log-downloader";
Console.WriteLine($"{appName} scaffold created.");
Console.WriteLine($"Mode: {cliOptions.Mode}");
if (cliOptions.StartDateUtc is not null || cliOptions.EndDateUtc is not null)
{
    Console.WriteLine($"Date range override: {cliOptions.StartDateUtc:yyyy-MM-dd} -> {cliOptions.EndDateUtc:yyyy-MM-dd}");
}
Console.WriteLine($"Configured local log root: {webLogOptions.SaveAllBlobsDirectory}");

var dateRangeResolver = new DateRangeResolver();
var dateRange = dateRangeResolver.Resolve(
    cliOptions.Mode,
    cliOptions.StartDateUtc,
    cliOptions.EndDateUtc,
    webLogOptions);
Console.WriteLine(
    $"Resolved date range ({dateRange.Source}): {dateRange.StartDateUtc:yyyy-MM-dd} -> {dateRange.EndDateUtc:yyyy-MM-dd}");

var blobClientFactory = new AzureBlobClientFactory();
var sourceTargets = blobClientFactory.BuildSourceTargets(webLogOptions);
Console.WriteLine($"Resolved source targets: {sourceTargets.Count}");
if (sourceTargets.Count > 0)
{
    var authMode = sourceTargets[0].AuthenticationMode;
    var uniqueContainers = sourceTargets
        .Select(target => target.ContainerUrl)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    Console.WriteLine($"Azure auth mode: {authMode}; containers: {uniqueContainers}; prefixes: {webLogOptions.PathPrefixes.Count}");
}

var templateResolver = new BlobPathTemplateResolver();
var sampleBlobPath = BuildSampleBlobPath(webLogOptions.PathPrefixes, dateRange.EndDateUtc);
var sampleBlobPathConflict = BuildSampleBlobPath(webLogOptions.PathPrefixes, dateRange.EndDateUtc, suffix: "sample-conflict.log");
if (templateResolver.TryResolve(
    sampleBlobPath,
    webLogOptions.BlobPathTemplates,
    out var sampleMatch,
    traceLogger: Console.WriteLine))
{
    var matchedCandidates = new List<BlobPathMatch> { sampleMatch! };

    if (templateResolver.TryResolve(
        sampleBlobPathConflict,
        webLogOptions.BlobPathTemplates,
        out var conflictMatch,
        traceLogger: Console.WriteLine))
    {
        matchedCandidates.Add(conflictMatch!);
    }

    var resolved = templateResolver.ResolveDeterministicConflicts(matchedCandidates);
    Console.WriteLine(
        $"Deterministic conflict strategy active: prefer lowest template order, then blob path sort. Keys resolved: {resolved.Count}");

    var persistenceService = new WebLogDownloadService();
    var persistence = await persistenceService.PersistAsync(webLogOptions.SaveAllBlobsDirectory!, resolved);
    Console.WriteLine($"Persistence root ready: {persistence.OutputRootPath}");
    Console.WriteLine($"Persisted files: {persistence.WrittenCount}; overwrites detected: {persistence.OverwrittenCount}");
    foreach (var persisted in persistence.Files)
    {
        Console.WriteLine($"- {persisted.LogicalKey} -> {persisted.FilePath}");
    }
}
else
{
    Console.WriteLine("No template match for sample path. Check Azure:WebLogs:BlobPathTemplates order and token structure.");
}

Console.WriteLine("Next step: implement download workflow services from task list.");

return;

static IConfigurationRoot BuildConfiguration(string? configPath)
{
    var baseDirectory = Directory.GetCurrentDirectory();
    var configFileName = "appsettings.json";
    var optional = true;

    if (!string.IsNullOrWhiteSpace(configPath))
    {
        baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))
            ?? Directory.GetCurrentDirectory();
        configFileName = Path.GetFileName(configPath);
        optional = false;
    }

    return new ConfigurationBuilder()
        .SetBasePath(baseDirectory)
        .AddJsonFile(configFileName, optional: optional, reloadOnChange: false)
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

static string BuildSampleBlobPath(IReadOnlyList<string> pathPrefixes, DateTime dateUtc, string suffix = "sample.log")
{
    var prefix = pathPrefixes.Count > 0
        ? pathPrefixes[0].TrimEnd('/')
        : "SAMPLE-INSTANCE";

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
