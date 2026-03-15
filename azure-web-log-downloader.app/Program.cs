using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var appName = "azure-web-log-downloader";
Console.WriteLine($"{appName} scaffold created.");
Console.WriteLine("Next step: define and implement commands in the generated PRD.");

var localRoot = configuration["Azure:WebLogs:SaveAllBlobsDirectory"] ?? "./logs";
Console.WriteLine($"Configured local log root: {localRoot}");
