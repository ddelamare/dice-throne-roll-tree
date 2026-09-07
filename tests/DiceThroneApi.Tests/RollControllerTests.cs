using System.IO;
using System.Threading.Tasks;
using DiceThroneApi.Controllers;
using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DiceThroneApi.Tests;

public class RollControllerTests
{
    private FakeWebHostEnvironment CreateTestEnvironment()
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
        using var env = CreateTestEnvironment();
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
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
        using var env = CreateTestEnvironment();
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
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
    public async Task Advice_ExcludeManifestDie_DoesNotUsePsylockeManifestDie()
    {
        using var env = CreateTestEnvironment();
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
        var heroService = new HeroService(env, parser);
        var telemetry = new TelemetryService(env);
        var controller = new RollController(heroService, advisor, calculator, simulator, parser, telemetry);
        var dice = new List<int> { 6, 6, 6, 6, 6, 1 };

        var includedResult = Assert.IsType<OkObjectResult>(await controller.GetAdvice(new AdviceRequest
        {
            HeroId = "psylocke",
            CurrentDice = dice,
            RollsRemaining = 0,
            Method = "analytic"
        }));
        var includedAdvice = Assert.IsType<List<RollAdvice>>(includedResult.Value);
        var includedOtherworlder = includedAdvice.Single(advice => advice.ObjectiveName == "Otherworlder!");

        var excludedResult = Assert.IsType<OkObjectResult>(await controller.GetAdvice(new AdviceRequest
        {
            HeroId = "psylocke",
            CurrentDice = dice,
            RollsRemaining = 0,
            Method = "analytic",
            ExcludeManifestDie = true
        }));
        var excludedAdvice = Assert.IsType<List<RollAdvice>>(excludedResult.Value);
        var excludedOtherworlder = excludedAdvice.Single(advice => advice.ObjectiveName == "Otherworlder!");

        Assert.Equal(1.0, includedOtherworlder.Probability);
        Assert.Equal(0.0, excludedOtherworlder.Probability);
        Assert.Equal(dice.Count, excludedOtherworlder.DiceToKeep.Count);
        Assert.True(excludedOtherworlder.DiceToKeep[0]);
    }

    [Fact]
    public async Task Simulate_RecordsTelemetryForOperationAndHero()
    {
        using var env = CreateTestEnvironment();
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
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

    [Fact]
    public async Task CalculateProbability_RecordsTelemetryForOperation()
    {
        using var env = CreateTestEnvironment();
        var parser = new DiceNotationParser();
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        var advisor = new DiceRollAdvisor(calculator, simulator);
        var telemetry = new TelemetryService(env);
        var controller = new RollController(new HeroService(env, parser), advisor, calculator, simulator, parser, telemetry)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Headers["X-Visitor-Id"] = "probability-visitor";

        var result = await controller.CalculateProbability(new ProbabilityRequest
        {
            Notation = "[6]",
            DiceCount = 5,
            Method = "analytic"
        });

        Assert.IsType<OkObjectResult>(result);

        var summary = await telemetry.GetSummaryAsync();
        Assert.Equal(1, summary.TotalOperations);
        Assert.Equal(1, summary.UniqueVisitors);
        Assert.Equal(1, summary.OperationCounts["probability"]);
        Assert.Empty(summary.HeroUsage);
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment, IDisposable
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider? WebRootFileProvider { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider? ContentRootFileProvider { get; set; }

        public void Dispose()
        {
            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
        }
    }
}
