using System.Text.Json;
using DiceThroneApi.Models;
using Microsoft.AspNetCore.Hosting;

namespace DiceThroneApi.Services;

public class TelemetryService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly TimeSpan DefaultFallbackDuration = TimeSpan.FromHours(1);

    private readonly string _telemetryPath;
    private readonly TimeSpan _fallbackDuration;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly List<TelemetryFileError> _fileErrors = [];
    private bool _useInMemoryFallback;
    private DateTimeOffset? _fallbackStartedAt;

    public TelemetryService(IWebHostEnvironment env, TimeSpan? fallbackDuration = null)
    {
        _telemetryPath = Path.Combine(env.ContentRootPath, "App_Data", "telemetry.json");
        _fallbackDuration = fallbackDuration ?? DefaultFallbackDuration;
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
            var summary = state.ToSummary();
            summary.FileErrors = new List<TelemetryFileError>(_fileErrors);
            return summary;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<TelemetryState> LoadStateAsync()
    {
        ClearFallbackIfExpired();

        if (_useInMemoryFallback)
        {
            return new TelemetryState();
        }

        if (!File.Exists(_telemetryPath))
        {
            return new TelemetryState();
        }

        try
        {
            await using var stream = File.OpenRead(_telemetryPath);
            var state = await JsonSerializer.DeserializeAsync<TelemetryState>(stream, JsonOptions);
            return state ?? new TelemetryState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            RecordFileError("load", exception);
            return new TelemetryState();
        }
    }

    private async Task SaveStateAsync(TelemetryState state)
    {
        ClearFallbackIfExpired();

        if (_useInMemoryFallback)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_telemetryPath);
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(_telemetryPath);
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            RecordFileError("save", exception);
        }
    }

    // Must be called while holding _mutex.
    private void ClearFallbackIfExpired()
    {
        if (_useInMemoryFallback &&
            _fallbackStartedAt.HasValue &&
            DateTimeOffset.UtcNow - _fallbackStartedAt.Value >= _fallbackDuration)
        {
            _useInMemoryFallback = false;
            _fallbackStartedAt = null;
        }
    }

    // Must be called while holding _mutex.
    private void RecordFileError(string operation, Exception exception)
    {
        if (!_useInMemoryFallback)
        {
            _useInMemoryFallback = true;
            _fallbackStartedAt = DateTimeOffset.UtcNow;
        }

        _fileErrors.Add(new TelemetryFileError(DateTimeOffset.UtcNow, operation, exception.Message));
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
        public DateTimeOffset? LastUpdatedUtc { get; set; }

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

    public void Dispose()
    {
        _mutex.Dispose();
    }
}
