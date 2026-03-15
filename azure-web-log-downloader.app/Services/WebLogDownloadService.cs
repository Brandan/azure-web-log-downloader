using Azure.Storage.Blobs;

namespace AzureWebLogDownloader.Services;

public sealed class WebLogDownloadService
{
    private readonly FileNameBuilder _fileNameBuilder = new();

    public async Task<PersistenceResult> PersistAsync(
        string outputRootDirectory,
        IReadOnlyCollection<BlobDownloadCandidate> matchedBlobs,
        string? fileNamePattern = null,
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

            var timestamp = matchedBlob.Match.TimestampUtc;
            var yearDirectory = Path.Combine(rootPath, timestamp.Year.ToString("D4"));
            Directory.CreateDirectory(yearDirectory);

            var fileName = _fileNameBuilder.BuildFileName(matchedBlob.Match.Instance, timestamp, fileNamePattern);
            var filePath = Path.Combine(yearDirectory, fileName);
            var logicalKey = _fileNameBuilder.BuildLogicalKey(matchedBlob.Match.Instance, timestamp);

            var existedBeforeWrite = File.Exists(filePath);
            if (writesByLogicalKey.ContainsKey(logicalKey) || existedBeforeWrite)
            {
                overwrittenInRun++;
            }

            var blobClient = matchedBlob.ContainerClient.GetBlobClient(matchedBlob.Match.BlobPath);
            await using (var sourceStream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            await using (var destinationStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            }

            writesByLogicalKey[logicalKey] = new PersistedLogFile(logicalKey, filePath, timestamp, matchedBlob.Match.BlobPath);
        }

        var persistedFiles = writesByLogicalKey.Values
            .OrderBy(file => file.LogicalKey, StringComparer.Ordinal)
            .ToList();

        return new PersistenceResult(
            rootPath,
            persistedFiles,
            WrittenCount: persistedFiles.Count,
            OverwrittenCount: overwrittenInRun);
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

public sealed record BlobDownloadCandidate(
    BlobContainerClient ContainerClient,
    BlobPathMatch Match);
