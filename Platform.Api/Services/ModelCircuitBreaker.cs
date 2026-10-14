using System.Collections.Concurrent;

namespace Platform.Api.Services;

/// <summary>
/// Remembers models that recently failed (overloaded, rate-limited, retired) so requests skip them
/// for a while instead of waiting for another 503 on every call. Singleton: state is shared by all requests.
/// </summary>
public class ModelCircuitBreaker(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _openUntil = new();

    public bool IsOpen(string model) =>
        _openUntil.TryGetValue(model, out var until) && until > time.GetUtcNow();

    public void Open(string model, TimeSpan duration) =>
        _openUntil[model] = time.GetUtcNow() + duration;

    public void Close(string model) =>
        _openUntil.TryRemove(model, out _);
}
