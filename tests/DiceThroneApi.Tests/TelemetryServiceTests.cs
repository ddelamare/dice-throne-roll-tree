using System.IO;
using System.Threading.Tasks;
using DiceThroneApi.Services;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace DiceThroneApi.Tests;

public class TelemetryServiceTests
{
    [Fact]
    public async Task RecordVisitAsync_TracksVisitsVisitorsAndPages()
    {
        var env = CreateTestEnvironment();
        var telemetry = new TelemetryService(env);

        await telemetry.RecordVisitAsync("visitor-a", "index");
        await telemetry.RecordVisitAsync("visitor-a", "telemetry");
        await telemetry.RecordVisitAsync("visitor-b", "index");

        var summary = await telemetry.GetSummaryAsync();

        Assert.Equal(3, summary.TotalVisits);
        Assert.Equal(2, summary.UniqueVisitors);
        Assert.Equal(2, summary.PageVisits["index"]);
        Assert.Equal(1, summary.PageVisits["telemetry"]);
    }

    [Fact]
    public async Task RecordOperationAsync_TracksOperationsHeroUsageAndPersists()
    {
        var env = CreateTestEnvironment();
        var telemetry = new TelemetryService(env);

        await telemetry.RecordOperationAsync("visitor-a", "simulate", "barbarian");
        await telemetry.RecordOperationAsync("visitor-a", "simulate", "barbarian");
        await telemetry.RecordOperationAsync("visitor-b", "advice", "moon-elf");

        var firstSummary = await telemetry.GetSummaryAsync();
        Assert.Equal(3, firstSummary.TotalOperations);
        Assert.Equal(2, firstSummary.UniqueVisitors);
        Assert.Equal(2, firstSummary.OperationCounts["simulate"]);
        Assert.Equal(1, firstSummary.OperationCounts["advice"]);
        Assert.Equal(2, firstSummary.HeroUsage["barbarian"]);
        Assert.Equal(1, firstSummary.HeroUsage["moon-elf"]);

        var reloaded = new TelemetryService(env);
        var secondSummary = await reloaded.GetSummaryAsync();
        Assert.Equal(3, secondSummary.TotalOperations);
        Assert.Equal(2, secondSummary.HeroUsage["barbarian"]);
    }

    private static IWebHostEnvironment CreateTestEnvironment()
    {
        var contentRootPath = Path.Combine(Path.GetTempPath(), "dice-throne-telemetry-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(contentRootPath);

        return new FakeWebHostEnvironment
        {
            ContentRootPath = contentRootPath,
            EnvironmentName = "Development",
            ApplicationName = "DiceThroneApi.Tests",
            WebRootPath = Path.Combine(contentRootPath, "wwwroot")
        };
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider? WebRootFileProvider { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider? ContentRootFileProvider { get; set; }
    }
}
