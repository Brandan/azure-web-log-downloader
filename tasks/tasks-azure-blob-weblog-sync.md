## Relevant Files

- `azure-web-log-downloader.app/Program.cs` - CLI entrypoint, argument parsing, orchestration, and console logging flow.
- `azure-web-log-downloader.app/appsettings.json` - Primary configuration source for Azure auth settings, source URLs/prefixes, templates, and defaults.
- `azure-web-log-downloader.app/azure-web-log-downloader.app.csproj` - Add Azure SDK and any supporting package references.
- `azure-web-log-downloader.app/Configuration/WebLogOptions.cs` - New strongly typed options model for `Azure:WebLogs` settings.
- `azure-web-log-downloader.app/Services/AzureBlobClientFactory.cs` - New auth-priority implementation (connection string first, `DefaultAzureCredential` fallback).
- `azure-web-log-downloader.app/Services/BlobPathTemplateResolver.cs` - New ordered template resolution logic for legacy/new blob layouts.
- `azure-web-log-downloader.app/Services/DateRangeResolver.cs` - New daily/weekly/default and explicit `--start`/`--end` range logic.
- `azure-web-log-downloader.app/Services/WebLogDownloadService.cs` - New end-to-end listing/filtering/downloading and idempotent overwrite workflow.
- `azure-web-log-downloader.app/Services/FileNameBuilder.cs` - New `{instance}-{yyyyMMdd_HHmm}.log` naming and key generation utility.
- `azure-web-log-downloader.tests/SanityTests.cs` - Replace/expand baseline tests to cover implemented workflow.
- `azure-web-log-downloader.tests/BlobPathTemplateResolverTests.cs` - Unit tests for template ordering and fallback behavior.
- `azure-web-log-downloader.tests/FileNameBuilderTests.cs` - Unit tests for filename format and logical key consistency.
- `azure-web-log-downloader.tests/WebLogDownloadServiceTests.cs` - Unit tests for idempotent overwrite behavior and failure handling.
- `azure-web-log-downloader.tests/azure-web-log-downloader.tests.csproj` - Add test dependencies/mocks for Azure and filesystem behaviors.
- `README.md` - Add CLI usage examples, configuration reference, and macOS scheduling instructions.
- `tasks/prd-azure-blob-weblog-sync.md` - Source requirements and acceptance criteria reference during implementation.

### Notes

- Unit tests should typically be placed alongside or near the code files they are testing; this repo currently uses the dedicated `azure-web-log-downloader.tests` project.
- Use `dotnet test` (or `dotnet test --filter <Name>`) to run tests for this .NET solution.
- Keep template behavior deterministic: evaluate `BlobPathTemplates[]` in configured order and stop on first valid match.
- Normalize datetime handling strategy early (prefer UTC unless product decision requires configurable timezone).

## Instructions for Completing Tasks

**IMPORTANT:** As you complete each task, you must check it off in this markdown file by changing `- [ ]` to `- [x]`. This helps track progress and ensures you don't skip any steps.

Example:
- `- [ ] 1.1 Read file` -> `- [x] 1.1 Read file` (after completing)

Update the file after completing each sub-task, not just after completing an entire parent task.

## Tasks

- [x] 0.0 Create feature branch
  - [x] 0.1 Create and checkout a new branch for this feature (for example `git checkout -b feature/azure-blob-weblog-sync`)
  - [x] 0.2 Confirm branch is active and clean before implementation work begins
- [x] 1.0 Define CLI contract and configuration model for Azure weblog sync
  - [x] 1.1 Define CLI options for `--mode daily|weekly`, `--start`, `--end`, and `--config` in `Program.cs`
  - [x] 1.2 Add a strongly typed `Azure:WebLogs` options model with all required properties from the PRD
  - [x] 1.3 Bind and validate configuration at startup with clear validation errors for missing critical settings
  - [x] 1.4 Add/update sample `appsettings.json` values showing multiple container URLs, prefixes, and ordered path templates
- [x] 2.0 Implement Azure blob source discovery with auth-priority fallback
  - [x] 2.1 Add Azure SDK dependencies in the app project if they are not already present
  - [x] 2.2 Implement client factory logic that tries connection string first when configured and valid
  - [x] 2.3 Implement fallback to `DefaultAzureCredential` when connection string is absent/invalid
  - [x] 2.4 Support iterating across multiple configured container URLs and prefixes in one run
  - [x] 2.5 Add explicit, actionable errors for common Azure failures (403, missing container, credential issues)
- [ ] 3.0 Implement date-scope filtering and ordered blob path-template resolution
  - [ ] 3.1 Implement date range resolver for daily default (yesterday), weekly default (last 7 days), and explicit start/end inputs
  - [ ] 3.2 Implement path template resolver that evaluates templates in configured order
  - [ ] 3.3 Ensure resolver supports both known current path layout and legacy/new layouts without code changes
  - [ ] 3.4 Define deterministic conflict behavior when multiple templates or blobs map to the same logical minute key
  - [ ] 3.5 Log which template matched for traceability in troubleshooting scenarios
- [ ] 4.0 Implement local file persistence layout, naming, and idempotent overwrite behavior
  - [ ] 4.1 Implement root output directory handling using `SaveAllBlobsDirectory`
  - [ ] 4.2 Implement per-year subdirectory creation under the output root
  - [ ] 4.3 Implement filename generation format `{instance}-{yyyyMMdd_HHmm}.log`
  - [ ] 4.4 Implement logical key behavior based on `(instance, datetime)` and overwrite when key already exists
  - [ ] 4.5 Ensure reruns for the same date range do not create duplicate logical files
- [ ] 5.0 Add operational logging, actionable error handling, and optional file logging toggle
  - [ ] 5.1 Add structured console progress logs for run start/end, container/prefix scanning, and download counts
  - [ ] 5.2 Add warning/error logs that include context needed to diagnose failures quickly
  - [ ] 5.3 Add optional file logging configuration switch and implementation (if enabled in v1 scope)
  - [ ] 5.4 Ensure non-fatal source errors are handled gracefully so other sources can continue when appropriate
- [ ] 6.0 Add automated tests and macOS scheduling documentation (`cron` and `launchd`)
  - [ ] 6.1 Add unit tests for ordered path template resolution behavior
  - [ ] 6.2 Add unit tests for filename and logical key generation format
  - [ ] 6.3 Add tests validating idempotent overwrite behavior on repeated downloads
  - [ ] 6.4 Add tests for auth fallback behavior and representative failure modes
  - [ ] 6.5 Update README with setup, config, run commands, and clear examples for daily/weekly/date-range execution
  - [ ] 6.6 Add macOS scheduler sections for both `cron` and `launchd`, including PATH/working-directory/credential-context caveats
  - [ ] 6.7 Run the full test suite and capture any follow-up fixes before finalizing
