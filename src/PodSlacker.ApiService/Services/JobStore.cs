using System.Collections.Concurrent;
using PodSlacker.ApiService.Models;

namespace PodSlacker.ApiService.Services;

/// <summary>
/// Thread-safe in-memory store for active and recently-completed jobs.
/// Jobs are evicted by <see cref="JobEvictionService"/> after a TTL expires.
/// </summary>
public sealed class JobStore
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();

    /// <summary>Creates a new <see cref="JobRecord"/> and adds it to the store.</summary>
    /// <returns>The newly created record.</returns>
    public JobRecord Create(string videoUrl)
    {
        var record = new JobRecord { VideoUrl = videoUrl };
        _jobs[record.Id] = record;
        return record;
    }

    /// <summary>Retrieves a job by <paramref name="id"/>, or <see langword="null"/> if not found.</summary>
    public JobRecord? Get(string id) => _jobs.TryGetValue(id, out var r) ? r : null;

    /// <summary>Returns a snapshot of all current jobs.</summary>
    public IReadOnlyCollection<JobRecord> All() => _jobs.Values.ToList().AsReadOnly();

    /// <summary>Removes jobs whose <see cref="JobRecord.UpdatedAt"/> is older than <paramref name="maxAge"/>.</summary>
    public int Evict(TimeSpan maxAge)
    {
        var cutoff  = DateTime.UtcNow - maxAge;
        var expired = _jobs.Values.Where(j => j.UpdatedAt < cutoff).Select(j => j.Id).ToList();
        foreach (var id in expired)
            _jobs.TryRemove(id, out _);
        return expired.Count;
    }
}
