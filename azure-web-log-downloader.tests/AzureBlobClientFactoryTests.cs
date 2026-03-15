using AzureWebLogDownloader.Configuration;
using AzureWebLogDownloader.Services;
using Xunit;

namespace AzureWebLogDownloader.Tests;

public class AzureBlobClientFactoryTests
{
    [Fact]
    public void BuildSourceTargets_UsesDefaultAzureCredential_WhenOnlyContainerUrlsConfigured()
    {
        var options = CreateOptions(
            connectionString: null,
            blobContainerUrls: ["https://exampleaccount.blob.core.windows.net/weblogs"],
            pathPrefixes: ["BF-PROD-APP-1-UC"]);
        var sut = new AzureBlobClientFactory();

        var result = sut.BuildSourceTargets(options);

        Assert.Single(result);
        Assert.All(result, target => Assert.Equal(AuthenticationMode.DefaultAzureCredential, target.AuthenticationMode));
    }

    [Fact]
    public void BuildSourceTargets_UsesConnectionString_WhenBothConnectionStringAndContainerUrlsConfigured()
    {
        var options = CreateOptions(
            connectionString: "UseDevelopmentStorage=true",
            blobContainerUrls: ["https://exampleaccount.blob.core.windows.net/weblogs"],
            pathPrefixes: ["BF-PROD-APP-1-UC"]);
        var sut = new AzureBlobClientFactory();

        var result = sut.BuildSourceTargets(options);

        Assert.Single(result);
        Assert.All(result, target => Assert.Equal(AuthenticationMode.ConnectionString, target.AuthenticationMode));
    }

    [Fact]
    public void BuildSourceTargets_ReturnsEmpty_WhenConnectionStringConfiguredWithoutContainerUrls()
    {
        var options = CreateOptions(
            connectionString: "UseDevelopmentStorage=true",
            blobContainerUrls: [],
            pathPrefixes: ["BF-PROD-APP-1-UC"]);
        var sut = new AzureBlobClientFactory();

        var result = sut.BuildSourceTargets(options);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildSourceTargets_ReturnsEmpty_WhenBothAreSetButConnectionStringIsInvalid()
    {
        var options = CreateOptions(
            connectionString: "AccountNameOnly=missing-fields",
            blobContainerUrls: ["https://exampleaccount.blob.core.windows.net/weblogs"],
            pathPrefixes: ["BF-PROD-APP-1-UC"]);
        var sut = new AzureBlobClientFactory();

        var result = sut.BuildSourceTargets(options);

        Assert.Empty(result);
    }

    private static WebLogOptions CreateOptions(
        string? connectionString,
        List<string> blobContainerUrls,
        List<string> pathPrefixes)
    {
        return new WebLogOptions
        {
            ConnectionString = connectionString,
            BlobContainerUrls = blobContainerUrls,
            PathPrefixes = pathPrefixes,
            BlobPathTemplates = ["{instance}/{yyyy}/{MM}/{dd}/{HH}/{filename}.log"],
            SaveAllBlobsDirectory = "./logs"
        };
    }
}
