# azure-web-log-downloader

Scaffold for a .NET command-line app focused on downloading Azure App Service web logs (W3C) from Blob Storage with configurable path formats and scheduled runs on macOS.

## Project Info

- App name: `azure-web-log-downloader`
- Bundle/Application identifier prefix: `net.thelloyds.`
- Full identifier used: `net.thelloyds.azure-web-log-downloader`
- Target framework in scaffold: `.NET 8` (`net8.0`)

## Publish Single Executable (Trimmed)

Use the publish helper to produce a trimmed, self-contained, single executable:

```bash
bash scripts/publish-single-file.sh
```

Optional arguments:

```bash
bash scripts/publish-single-file.sh <RID> <Configuration> <OutputDir>
```

Example:

```bash
bash scripts/publish-single-file.sh osx-x64 Release ./publish/osx-x64
```

## Configuration Resolution Order

At startup, configuration is loaded in this order (later sources override earlier sources):

1. `appsettings.json` (optional)
2. `~/.azure-web-log-downloader` (optional global default config)
3. `./.azure-web-log-downloader` (optional project-local default config)
4. `--config <path>` file (optional explicit config)
5. environment variables

CLI arguments such as `--mode`, `--start`, and `--end` then override runtime behavior after config is loaded.

## Example Default Config File

An example default config file is provided at:

- `.azure-web-log-downloader.config.example`

Copy it to `./.azure-web-log-downloader` and fill in your values:

```bash
cp .azure-web-log-downloader.config.example .azure-web-log-downloader
```

## Prompt And Description Used

The following is the full prompt/description provided when creating this project:

```text
/dotnet-app  .NET CLI to download Azure Storage Blob web logs with configurable path format and scheduled runs

Summary:
A .NET command-line application that downloads Azure App Service web logs (W3C) from one or more Azure Blob Storage locations. It supports a changed blob path format (to be specified in config or docs), runs on a schedule (daily or weekly), and writes all files into a single local directory organized by year, with filenames that include instance name and a datetime in a format consistent with our existing tools (e.g. yyyyMMdd and optionally time). Re-downloading the same logical file (same instance + same datetime) overwrites the existing file so runs are idempotent. The app should be easy to install and run as a scheduled job on macOS (cron and/or launchd), with clear install and scheduling instructions.

Functional requirements

Download source

Support multiple locations: multiple blob container URLs and/or multiple path prefixes within a container (e.g. instance folders like BF-PROD-APP-1-UC, BF-PROD-APP-1-UC__1314).
Authentication: Azure connection string and/or container URL with DefaultAzureCredential (or API key), consistent with existing Azure web log usage in the same solution.
Blob path format

Current format in our codebase: {instance}/YYYY/MM/DD/HH/filename.log (e.g. BF-PROD-APP-1-UC/2026/02/02/21/dc42c7.log).
Log path format has changed in Azure; the app must support the new path format (structure to be documented in the PRD or config, e.g. optional prefix pattern or template so it can be adapted when the exact new layout is known).
Local storage layout

Single root directory for all downloaded logs (configurable path).
Under that root: organize by year (e.g. {root}/2026/).
File naming: include instance name and datetime in a format similar to our existing reports (e.g. yyyyMMdd, and optionally time such as yyyyMMdd_HHmm or yyyyMMdd-HHmm), so that:
Each file is uniquely identified by instance + datetime.
Names are consistent with patterns like system-overview-{yyyyMMdd}.md and analysis-{yyyyMMdd}.json used elsewhere in the project.
Idempotent runs

If a file with the same instance and same datetime already exists locally, overwrite it (no duplicate copies; re-run is safe).
Allow running the tool multiple times per day or week without creating duplicate files for the same blob.
CLI behavior

Subcommands or options to:
Download for a date range (e.g. last 1 day, last 7 days, or explicit start/end date) for daily vs weekly use.
Configuration via appsettings.json (or similar) for:
Blob container URL(s) and/or connection string.
Path prefixes / instance list (and new path format if needed).
Local root directory.
Optional: date range defaults (e.g. “yesterday” for daily, “last 7 days” for weekly).
Non-functional

Target .NET 8 (or same as bf-system-overview-cli).
Logging (console and optionally file) for progress and errors.
Clear error messages when blob access fails (e.g. 403, missing container).
Deliverables

Working .NET CLI (build, run, and basic tests).
Install and run instructions for macOS, including:
How to install the app (e.g. dotnet run, self-contained publish, or global tool).
How to run it as a scheduled job on a Mac:
cron: example crontab for daily (e.g. after midnight) and weekly runs.
launchd: example plist for a LaunchAgent that runs the command daily or weekly, with working directory and environment (e.g. PATH, Azure identity) if needed.
How to pass config (e.g. config file path, or current directory appsettings) when run from cron/launchd.
References in codebase

Blob listing and path structure: AzureWebLogService.cs (prefixes {instance}/YYYY/MM/DD/HH, GetWebLogPathPrefixes, SanitizeBlobNameForFile).
Config: appsettings.json under Azure:WebLogs (e.g. BlobContainerUrl, PathPrefix/PathPrefixes, SaveAllBlobsDirectory).
Date formatting: yyyyMMdd and yyyyMMdd_HHmmss in OutputHandler.cs, HistoryService.cs (e.g. system-overview-{yyyyMMdd}, analysis-{yyyyMMdd}.json).
```

## Notes

- Icon examples are under `assets/icons/examples`.
- A macOS app icon set scaffold is under `assets/icons/Assets.xcassets/AppIcon.appiconset`.
- PRD file will be created under `tasks/` after clarifying answers are provided.
