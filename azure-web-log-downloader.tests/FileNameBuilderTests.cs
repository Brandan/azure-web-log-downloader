using AzureWebLogDownloader.Services;
using Xunit;

namespace AzureWebLogDownloader.Tests;

public class FileNameBuilderTests
{
    [Fact]
    public void BuildFileName_UsesDefaultPattern_WhenPatternNotProvided()
    {
        var sut = new FileNameBuilder();
        var timestamp = new DateTime(2026, 1, 15, 18, 0, 0, DateTimeKind.Utc);

        var fileName = sut.BuildFileName("BF-PROD-APP-1-UC", timestamp);

        Assert.Equal("BF-PROD-APP-1-UC-20260115_1800.log", fileName);
    }

    [Fact]
    public void BuildFileName_UsesConfiguredPattern_WhenProvided()
    {
        var sut = new FileNameBuilder();
        var timestamp = new DateTime(2026, 1, 15, 18, 0, 0, DateTimeKind.Utc);

        var fileName = sut.BuildFileName("BF-PROD-APP-1-UC", timestamp, "{yyyy}-{MM}-{dd}-{HH}.log");

        Assert.Equal("2026-01-15-18.log", fileName);
    }

    [Fact]
    public void BuildFileName_InjectsInstanceToken_WhenRequested()
    {
        var sut = new FileNameBuilder();
        var timestamp = new DateTime(2026, 1, 15, 18, 0, 0, DateTimeKind.Utc);

        var fileName = sut.BuildFileName("BF-PROD-APP-1-UC", timestamp, "{instance}-{yyyy}-{MM}-{dd}.log");

        Assert.Equal("BF-PROD-APP-1-UC-2026-01-15.log", fileName);
    }
}
