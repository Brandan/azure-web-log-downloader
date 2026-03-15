using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using AzureWebLogDownloader.Configuration;

namespace AzureWebLogDownloader.Services;

public sealed class AzureBlobClientFactory
{
    public IReadOnlyList<BlobSourceTarget> BuildSourceTargets(WebLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var containerUrls = options.BlobContainerUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var prefixes = options.PathPrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(NormalizePrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (containerUrls.Count == 0 || prefixes.Count == 0)
        {
            return [];
        }

        var hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
        BlobServiceClient? blobServiceClient = null;
        TokenCredential? credential = null;
        AuthenticationMode authMode;

        if (hasConnectionString)
        {
            // Connection-string mode is used only when both URLs and a connection string are set.
            if (!TryCreateConnectionStringServiceClient(options.ConnectionString, out blobServiceClient))
            {
                return [];
            }

            authMode = AuthenticationMode.ConnectionString;
        }
        else
        {
            credential = new DefaultAzureCredential();
            authMode = AuthenticationMode.DefaultAzureCredential;
        }

        var targets = new List<BlobSourceTarget>();
        foreach (var containerUrl in containerUrls)
        {
            var containerClient = CreateContainerClient(containerUrl, blobServiceClient, credential);
            foreach (var prefix in prefixes)
            {
                targets.Add(new BlobSourceTarget(containerClient, containerUrl, prefix, authMode));
            }
        }

        return targets;
    }

    public async Task VerifyContainerAccessAsync(BlobContainerClient containerClient, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await containerClient.ExistsAsync(cancellationToken).ConfigureAwait(false);
            if (!response.Value)
            {
                throw new InvalidOperationException(
                    $"Azure container '{containerClient.Name}' was not found at '{containerClient.Uri}'. " +
                    "Confirm the URL/container name and that the account contains this container.");
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            throw new InvalidOperationException(
                $"Azure access denied (403) for container '{containerClient.Name}' at '{containerClient.Uri}'. " +
                "Verify RBAC roles, SAS permissions, or the supplied connection string credentials.",
                ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"Azure container '{containerClient.Name}' was not found (404) at '{containerClient.Uri}'. " +
                "Check the configured BlobContainerUrls and container names.",
                ex);
        }
        catch (CredentialUnavailableException ex)
        {
            throw new InvalidOperationException(
                "No Azure credential source was available for DefaultAzureCredential. " +
                "Set environment credentials, use managed identity, or provide a valid connection string.",
                ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new InvalidOperationException(
                "DefaultAzureCredential authentication failed. Sign in with Azure CLI (`az login`) " +
                "or configure managed identity/environment credentials before running the downloader.",
                ex);
        }
    }

    private static BlobContainerClient CreateContainerClient(
        string containerUrl,
        BlobServiceClient? blobServiceClient,
        TokenCredential? credential)
    {
        if (!Uri.TryCreate(containerUrl, UriKind.Absolute, out var parsedUri))
        {
            throw new InvalidOperationException(
                $"Invalid container URL '{containerUrl}'. Use an absolute blob container URL like " +
                "'https://<account>.blob.core.windows.net/<container>'.");
        }

        var containerName = ExtractContainerName(parsedUri);
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException(
                $"Container URL '{containerUrl}' does not include a valid container segment.");
        }

        if (blobServiceClient is not null)
        {
            return blobServiceClient.GetBlobContainerClient(containerName);
        }

        if (credential is null)
        {
            throw new InvalidOperationException(
                "Unable to create container client: no valid connection string or fallback credential available.");
        }

        return new BlobContainerClient(parsedUri, credential);
    }

    private static bool TryCreateConnectionStringServiceClient(
        string? connectionString,
        out BlobServiceClient? blobServiceClient)
    {
        blobServiceClient = null;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            blobServiceClient = new BlobServiceClient(connectionString.Trim());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizePrefix(string prefix)
    {
        var normalized = prefix.Trim();
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : $"{normalized}/";
    }

    private static string ExtractContainerName(Uri containerUri)
    {
        var path = containerUri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var slashIndex = path.IndexOf('/');
        return slashIndex < 0 ? path : path[..slashIndex];
    }
}

public sealed record BlobSourceTarget(
    BlobContainerClient ContainerClient,
    string ContainerUrl,
    string PathPrefix,
    AuthenticationMode AuthenticationMode);

public enum AuthenticationMode
{
    ConnectionString,
    DefaultAzureCredential
}
