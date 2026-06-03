using System.Text.Json;
using DiceThroneApi.Models;
using Microsoft.AspNetCore.Hosting;

namespace DiceThroneApi.Services;

public class TelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _telemetryPath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public TelemetryService(IWebHostEnvironment env)
    {
        _telemetryPath = Path.Combine(env.ContentRootPath, "App_Data", "telemetry.json");
    }

    public async Task RecordVisitAsync(string? visitorId, string? page)
    {
        await _mutex.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            state.TotalVisits++;
            TrackVisitor(state, visitorId);

            var normalizedPage = NormalizeKey(page);
            if (!string.IsNullOrWhiteSpace(normalizedPage))
            {
                state.PageVisits[normalizedPage] = state.PageVisits.GetValueOrDefault(normalizedPage) + 1;
            }

            state.LastUpdatedUtc = DateTimeOffset.UtcNow;
            await SaveStateAsync(state);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task RecordOperationAsync(string? visitorId, string operation, string? heroId = null)
    {
        await _mutex.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            state.TotalOperations++;
            TrackVisitor(state, visitorId);

            var normalizedOperation = NormalizeKey(operation);
            if (!string.IsNullOrWhiteSpace(normalizedOperation))
            {
                state.OperationCounts[normalizedOperation] = state.OperationCounts.GetValueOrDefault(normalizedOperation) + 1;
            }

            var normalizedHeroId = NormalizeKey(heroId);
            if (!string.IsNullOrWhiteSpace(normalizedHeroId))
            {
                state.HeroUsage[normalizedHeroId] = state.HeroUsage.GetValueOrDefault(normalizedHeroId) + 1;
            }

            state.LastUpdatedUtc = DateTimeOffset.UtcNow;
            await SaveStateAsync(state);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<TelemetrySummary> GetSummaryAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            return state.ToSummary();
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<TelemetryState> LoadStateAsync()
    {
        if (!File.Exists(_telemetryPath))
        {
            return new TelemetryState();
        }

        await using var stream = File.OpenRead(_telemetryPath);
        var state = await JsonSerializer.DeserializeAsync<TelemetryState>(stream, JsonOptions);
        return state ?? new TelemetryState();
    }

    private async Task SaveStateAsync(TelemetryState state)
    {
        var directory = Path.GetDirectoryName(_telemetryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_telemetryPath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
    }

    private static void TrackVisitor(TelemetryState state, string? visitorId)
    {
        var normalizedVisitorId = NormalizeVisitorId(visitorId);
        if (!string.IsNullOrWhiteSpace(normalizedVisitorId))
        {
            state.UniqueVisitorIds.Add(normalizedVisitorId);
        }
    }

    private static string? NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static string? NormalizeVisitorId(string? visitorId)
    {
        if (string.IsNullOrWhiteSpace(visitorId))
        {
            return null;
        }

        var normalized = visitorId.Trim();
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }

    private sealed class TelemetryState
    {
        public int TotalVisits { get; set; }
        public int TotalOperations { get; set; }
        public HashSet<string> UniqueVisitorIds { get; set; } = [];
        public Dictionary<string, int> OperationCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> HeroUsage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> PageVisits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public TelemetrySummary ToSummary()
        {
            return new TelemetrySummary
            {
                TotalVisits = TotalVisits,
                UniqueVisitors = UniqueVisitorIds.Count,
                TotalOperations = TotalOperations,
                OperationCounts = new Dictionary<string, int>(OperationCounts, StringComparer.OrdinalIgnoreCase),
                HeroUsage = new Dictionary<string, int>(HeroUsage, StringComparer.OrdinalIgnoreCase),
                PageVisits = new Dictionary<string, int>(PageVisits, StringComparer.OrdinalIgnoreCase),
                LastUpdatedUtc = LastUpdatedUtc
            };
        }
    }
}
