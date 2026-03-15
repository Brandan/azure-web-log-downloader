# PRD: Azure Blob Weblog Sync CLI

## 1) Introduction / Overview

`azure-web-log-downloader` is a .NET CLI that downloads Azure App Service W3C web logs from Azure Blob Storage into a single local root directory. It supports multiple containers and/or prefixes, handles both legacy and new blob path layouts through configurable path templates, and is intended to run on schedules (daily/weekly) on macOS.

The goal is to provide a reliable, repeatable log collection tool that is idempotent, easy to configure, and simple for operations teams to schedule.

## 2) Goals

1. Download web log blobs from one or more configured Azure sources into local storage.
2. Support both authentication approaches in v1 with deterministic priority.
3. Support multiple blob path templates, tried in configured order, so path-layout changes do not break ingestion.
4. Save files in a year-based local folder layout with consistent naming format: `{instance}-{yyyyMMdd_HHmm}.log`.
5. Ensure idempotent behavior by overwriting the same logical file (same instance + same datetime).
6. Provide clear install/run instructions for macOS, including `cron` and `launchd`.

## 3) User Stories

- As an operations engineer, I want to download logs from several app instances and prefixes in one run so I can centralize analysis.
- As a maintainer, I want to configure both connection string and managed identity auth so deployments can work in different environments.
- As a maintainer, I want path-template fallback behavior so the tool continues to work when Azure log path format changes.
- As a user running daily jobs, I want default lookback windows so I can schedule without custom date arguments every time.
- As a user rerunning jobs, I want duplicate logical files to be overwritten so reruns are safe and do not create clutter.

## 4) Functional Requirements

1. The system must provide a CLI entrypoint named `azure-web-log-downloader`.
2. The system must support downloading from multiple source locations, including:
   - multiple container URLs, and/or
   - multiple path prefixes within each container.
3. The system must support authentication modes in v1:
   - connection string, and
   - `DefaultAzureCredential`.
4. The system must support both auth modes in one config, with explicit priority order:
   - first: connection string (if present and valid),
   - second: `DefaultAzureCredential` (fallback).
5. The system must support configurable blob path templates and try multiple templates in order (first matching/working template wins).
6. The system must preserve support for current known format patterns and allow new format adoption via config only (no code change required for common layout updates).
7. The system must provide CLI options/subcommands for date scopes:
   - daily (default lookback = yesterday),
   - weekly (default lookback = last 7 days),
   - explicit `--start` and `--end` date range.
8. The system must store all downloaded files under one configurable local root directory.
9. The system must create per-year subdirectories under local root (for example: `{root}/2026/`).
10. The system must name each downloaded file using `{instance}-{yyyyMMdd_HHmm}.log`.
11. The system must treat the pair `(instance, datetime)` as the logical file key for idempotency.
12. The system must overwrite existing local files when the same logical key is downloaded again.
13. The system must log progress and failures to console.
14. The system should optionally support file logging (configurable).
15. The system must emit clear, actionable errors for Azure access failures (including 403, missing container, invalid credentials, or path not found).
16. The system must load configuration from `appsettings.json` (and optionally environment variable overrides).
17. The system must include basic automated tests for key logic:
   - path template resolution,
   - filename generation,
   - idempotent overwrite behavior.
18. The system must include macOS scheduling guidance for both `cron` and `launchd`.

## 5) Non-Goals (Out of Scope)

- Real-time log streaming or tailing.
- Parsing/analyzing log content beyond download and naming.
- Deduplication using blob content hash across different instance/datetime keys.
- Building a GUI interface.
- Multi-OS scheduler documentation beyond macOS (`cron`/`launchd` only for v1 docs).

## 6) Design Considerations

- Keep CLI UX simple and script-friendly (`--mode daily|weekly`, `--start`, `--end`, `--config`).
- Keep output naming consistent with existing ecosystem date conventions (`yyyyMMdd` family), specifically using `yyyyMMdd_HHmm`.
- Ensure docs include examples that match operational usage:
  - nightly run after midnight,
  - weekly run on a chosen day/time.
- Include sample config showing multiple templates in order, for example:
  - `{instance}/{yyyy}/{MM}/{dd}/{HH}/{filename}.log`
  - `{instance}/{yyyy}/{MM}/{dd}/{filename}.log`
  - `{prefix}/{instance}/{yyyy}/{MM}/{dd}/{HH}/{filename}.log`

## 7) Technical Considerations

- Target framework: .NET 8.
- Primary Azure SDK dependencies:
  - `Azure.Storage.Blobs`
  - `Azure.Identity`
- Suggested configuration shape under `Azure:WebLogs`:
  - `ConnectionString`
  - `BlobContainerUrls[]`
  - `PathPrefixes[]`
  - `BlobPathTemplates[]` (ordered)
  - `SaveAllBlobsDirectory`
  - `DefaultDailyLookbackDays` (1)
  - `DefaultWeeklyLookbackDays` (7)
- Preserve compatibility with existing codebase patterns referenced by the request:
  - blob prefix/date segment conventions,
  - output date-format consistency.
- Scheduling docs must call out environment constraints for `launchd`/`cron` (PATH, working directory, credentials context).

## 8) Success Metrics

1. Operational: scheduled daily and weekly jobs run successfully for 14 consecutive days in a test environment.
2. Data correctness: rerunning any date window does not increase unique logical file count for already-downloaded logs.
3. Coverage: at least one successful download path validated for each configured auth mode.
4. Usability: setup from README to first successful download can be completed by a junior developer/operator in under 30 minutes.
5. Reliability: clear error messages are produced for known failure classes (auth denied, container missing, misconfigured template).

## 9) Open Questions

1. For template matching, should templates be interpreted as:
   - strict path parser patterns,
   - glob-like prefix strategies,
   - or a combined parser + prefix approach?
2. When multiple blobs map to the same `(instance, yyyyMMdd_HHmm)` key within the same minute, which blob should win:
   - last modified newest,
   - lexical path order,
   - deterministic template order?
3. Should optional file logging be included in v1 implementation or deferred to v1.1?
4. Should timezone handling for datetime extraction/formatting always be UTC, or configurable local timezone?
5. Should CLI support a dry-run mode in v1 to preview download targets without writing files?
