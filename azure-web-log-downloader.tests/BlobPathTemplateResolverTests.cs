using AzureWebLogDownloader.Services;
using Xunit;

namespace AzureWebLogDownloader.Tests;

public class BlobPathTemplateResolverTests
{
    [Fact]
    public void TryResolve_MatchesAzureResourceIdPathFormat()
    {
        var resolver = new BlobPathTemplateResolver();
        var blobPath = "resourceId=/SUBSCRIPTIONS/AADB50F2-449C-4A30-90AB-D0E783717C69/RESOURCEGROUPS/BLUEFOLDER-PROD/PROVIDERS/MICROSOFT.WEB/SITES/BF-PROD-APP-1-UC/y=2026/m=03/d=14/h=00/m=00/PT1H.json";
        var templates = new List<string>
        {
            "resourceId=/SUBSCRIPTIONS/AADB50F2-449C-4A30-90AB-D0E783717C69/RESOURCEGROUPS/BLUEFOLDER-PROD/PROVIDERS/MICROSOFT.WEB/SITES/{instance}/y={yyyy}/m={MM}/d={dd}/h={HH}/m=00/{filename}"
        };

        var matched = resolver.TryResolve(blobPath, templates, out var match);

        Assert.True(matched);
        Assert.NotNull(match);
        Assert.Equal("BF-PROD-APP-1-UC", match!.Instance);
        Assert.Equal(new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc), match.TimestampUtc);
        Assert.Equal("PT1H.json", match.FileName);
    }
}
