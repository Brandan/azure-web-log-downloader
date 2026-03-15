namespace AzureWebLogDownloader.Services;

public sealed class WebLogDownloadService
{
    private readonly FileNameBuilder _fileNameBuilder = new();

    public async Task<PersistenceResult> PersistAsync(
        string outputRootDirectory,
        IReadOnlyCollection<BlobPathMatch> matchedBlobs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputRootDirectory))
        {
            throw new InvalidOperationException("Output root directory is required for local persistence.");
        }

        var rootPath = Path.GetFullPath(outputRootDirectory);
        Directory.CreateDirectory(rootPath);

        var writesByLogicalKey = new Dictionary<string, PersistedLogFile>(StringComparer.Ordinal);
        var overwrittenInRun = 0;

        foreach (var matchedBlob in matchedBlobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = matchedBlob.TimestampUtc;
            var yearDirectory = Path.Combine(rootPath, timestamp.Year.ToString("D4"));
            Directory.CreateDirectory(yearDirectory);

            var fileName = _fileNameBuilder.BuildFileName(matchedBlob.Instance, timestamp);
            var filePath = Path.Combine(yearDirectory, fileName);
            var logicalKey = _fileNameBuilder.BuildLogicalKey(matchedBlob.Instance, timestamp);

            var existedBeforeWrite = File.Exists(filePath);
            if (writesByLogicalKey.ContainsKey(logicalKey) || existedBeforeWrite)
            {
                overwrittenInRun++;
            }

            // Placeholder content until download stream integration lands in Task 5+.
            var content = $"blobPath={matchedBlob.BlobPath}\ninstance={matchedBlob.Instance}\ntimestampUtc={timestamp:O}\n";
            await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);

            writesByLogicalKey[logicalKey] = new PersistedLogFile(logicalKey, filePath, timestamp, matchedBlob.BlobPath);
        }

        var persistedFiles = writesByLogicalKey.Values
            .OrderBy(file => file.LogicalKey, StringComparer.Ordinal)
            .ToList();

        return new PersistenceResult(
            rootPath,
            persistedFiles,
            writtenCount: persistedFiles.Count,
            overwrittenCount: overwrittenInRun);
    }
}

public sealed record PersistedLogFile(
    string LogicalKey,
    string FilePath,
    DateTime TimestampUtc,
    string SourceBlobPath);

public sealed record PersistenceResult(
    string OutputRootPath,
    IReadOnlyList<PersistedLogFile> Files,
    int WrittenCount,
    int OverwrittenCount);
