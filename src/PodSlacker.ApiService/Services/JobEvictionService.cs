using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PodSlacker.ApiService.Services;

/// <summary>
/// Background service that periodically evicts stale jobs from <see cref="JobStore"/>.
/// Jobs are removed once they haven't been updated for more than <see cref="JobTtl"/>.
/// </summary>
public sealed class JobEvictionService(
    JobStore                   store,
    ILogger<JobEvictionService> logger) : BackgroundService
{
    /// <summary>How long a job is kept after its last update before being evicted.</summary>
    private static readonly TimeSpan JobTtl           = TimeSpan.FromHours(1);

    /// <summary>How often the eviction sweep runs.</summary>
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Job eviction service started (TTL={Ttl}, interval={Interval})",
            JobTtl, EvictionInterval);

        using var timer = new PeriodicTimer(EvictionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                int evicted = store.Evict(JobTtl);
                if (evicted > 0)
                    logger.LogInformation("Evicted {Count} expired job(s)", evicted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during job eviction sweep");
            }
        }
    }
}
