using System.IO;
using System.Threading.Tasks;
using DiceThroneApi.Controllers;
using DiceThroneApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DiceThroneApi.Tests;

public class RollControllerTests
{
    private IWebHostEnvironment CreateTestEnvironment()
    {
        var sourceRootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "DiceThroneApi"));
        var contentRootPath = Path.Combine(Path.GetTempPath(), "dice-throne-roll-controller-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(contentRootPath);

        var sourceHeroesPath = Path.Combine(sourceRootPath, "Data", "heroes");
        var targetHeroesPath = Path.Combine(contentRootPath, "Data", "heroes");
        Directory.CreateDirectory(targetHeroesPath);

        foreach (var heroFile in Directory.GetFiles(sourceHeroesPath, "*.json"))
        {
            File.Copy(heroFile, Path.Combine(targetHeroesPath, Path.GetFileName(heroFile)), overwrite: true);
        }

        return new FakeWebHostEnvironment
        {
            ContentRootPath = contentRootPath,
            EnvironmentName = "Development",
            ApplicationName = "DiceThroneApi",
            WebRootPath = Path.Combine(contentRootPath, "wwwroot")
        };
    }

    [Fact]
    public async Task SetDice_ReturnsOkAndAdviceForCustomDice()
    {
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
        var env = CreateTestEnvironment();
        var heroService = new HeroService(env, parser);
        var telemetry = new TelemetryService(env);
        var controller = new RollController(heroService, advisor, calculator, simulator, parser, telemetry);

        var request = new SetDiceRequest
        {
            HeroId = "barbarian",
            CurrentDice = new System.Collections.Generic.List<int> {1, 2, 3, 4, 5},
            RollsRemaining = 2,
            Method = "analytic"
        };

        var result = await controller.SetDice(request);
        var okResult = Assert.IsType<OkObjectResult>(result);

        Assert.NotNull(okResult.Value);
        var data = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        Assert.Contains("dice", data);
        Assert.Contains("rollsRemaining", data);
        Assert.Contains("suggestions", data);
    }

    [Fact]
    public async Task SetDice_HonorsRollsRemainingFromRequest()
    {
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
        var env = CreateTestEnvironment();
        var heroService = new HeroService(env, parser);
        var telemetry = new TelemetryService(env);
        var controller = new RollController(heroService, advisor, calculator, simulator, parser, telemetry);

        var request = new SetDiceRequest
        {
            HeroId = "barbarian",
            CurrentDice = new System.Collections.Generic.List<int> {5, 5, 5, 5, 5},
            RollsRemaining = 1,
            Method = "analytic"
        };

        var result = await controller.SetDice(request);
        var okResult = Assert.IsType<OkObjectResult>(result);

        var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(okResult.Value));
        Assert.Equal(1, document.RootElement.GetProperty("rollsRemaining").GetInt32());
    }

    [Fact]
    public async Task Simulate_RecordsTelemetryForOperationAndHero()
    {
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
        var env = CreateTestEnvironment();
        var heroService = new HeroService(env, parser);
        var telemetry = new TelemetryService(env);
        var controller = new RollController(heroService, advisor, calculator, simulator, parser, telemetry)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Headers["X-Visitor-Id"] = "test-visitor";

        var result = await controller.Simulate(new SimulateRequest
        {
            HeroId = "barbarian",
            DiceCount = 5,
            Method = "analytic"
        });

        Assert.IsType<OkObjectResult>(result);

        var summary = await telemetry.GetSummaryAsync();
        Assert.Equal(1, summary.TotalOperations);
        Assert.Equal(1, summary.UniqueVisitors);
        Assert.Equal(1, summary.OperationCounts["simulate"]);
        Assert.Equal(1, summary.HeroUsage["barbarian"]);
    }

    private class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider? WebRootFileProvider { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider? ContentRootFileProvider { get; set; }
    }
}
