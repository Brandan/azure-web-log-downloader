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
    var mode = "daily";
    string? configPath = null;
    DateTime? startDateUtc = null;
    DateTime? endDateUtc = null;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--mode":
                mode = ReadValue(args, ref i, arg);
                if (!mode.Equals("daily", StringComparison.OrdinalIgnoreCase) &&
                    !mode.Equals("weekly", StringComparison.OrdinalIgnoreCase))
                {
                    ExitWithCliError("Invalid --mode. Supported values are: daily, weekly.");
                }
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
        mode.ToLowerInvariant(),
        configPath,
        startDateUtc,
        endDateUtc);
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
    Environment.Exit(1);
}

internal sealed record CliOptions(
    string Mode,
    string? ConfigPath,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc);
