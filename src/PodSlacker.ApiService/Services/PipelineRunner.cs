using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PodSlacker.ApiService.Models;
using PodSlacker.Core.Models;
using PodSlacker.Core.Pipeline;
using PodSlacker.Core.Services;

namespace PodSlacker.ApiService.Services;

/// <summary>
/// Executes a PodSlacker pipeline run inside a background <see cref="Task"/>,
/// updating the associated <see cref="JobRecord"/> as progress is reported.
/// </summary>
public sealed class PipelineRunner(
    IServiceScopeFactory        scopeFactory,
    ILogger<PipelineRunner>     logger)
{
    /// <summary>
    /// Starts the pipeline for <paramref name="job"/> in the background.
    /// The method returns immediately; the pipeline runs on a thread-pool thread.
    /// </summary>
    /// <param name="job">The job record to update with progress.</param>
    /// <param name="videoUrl">YouTube URL to process.</param>
    /// <param name="config">Runtime configuration from the client.</param>
    public void StartBackground(JobRecord job, string videoUrl, PodSlackerConfig config)
    {
        // Fire-and-forget; exceptions are caught and recorded in the job record.
        _ = Task.Run(() => RunAsync(job, videoUrl, config));
    }

    private async Task RunAsync(JobRecord job, string videoUrl, PodSlackerConfig config)
    {
        // Create a temporary output directory; cleaned up after HTML is read.
        string tempDir = Path.Combine(Path.GetTempPath(), "podslacker", job.Id);
        Directory.CreateDirectory(tempDir);

        // Override OutputDir; preserve PublishGithub so the web UI can request publishing.
        config = config with { OutputDir = tempDir };

        job.Status    = JobStatus.Running;
        job.Message   = "Starting pipeline…";
        job.UpdatedAt = DateTime.UtcNow;

        try
        {
            // Build clients from config — API keys must be set in server environment.
            var chatClient  = PipelineClientFactory.CreateChatClient(config);
            var audioClient = PipelineClientFactory.CreateAudioClient(config);

            // Create a fresh DI scope for this background task.
            // We cannot reuse the request-scoped IServiceProvider — it is disposed
            // as soon as the HTTP handler returns the 202 Accepted response.
            await using var scope = scopeFactory.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<PodSlackerPipeline>();

            var progress = new Progress<PipelineProgress>(p =>
            {
                // Capture the video title as soon as the pipeline resolves it
                // (reported on the FetchingTitle step before any LLM calls start).
                if (p.Title is { Length: > 0 })
                    job.Title = p.Title;

                job.Status    = JobStatus.Running;
                job.Message   = p.Message;
                job.Percent   = p.PercentComplete;
                job.UpdatedAt = DateTime.UtcNow;
                logger.LogInformation("[{JobId}] {Pct}% — {Msg}", job.Id, p.PercentComplete, p.Message);
            });

            var result = await pipeline.RunAsync(
                videoUrl, config, chatClient, audioClient, progress);

            // Capture title and GitHub Pages URL from the completed result.
            job.Title          = result.Title;
            job.GitHubPagesUrl = result.GitHubPagesUrl;

            // Read generated HTML into memory then clean up the temp dir.
            if (result.HtmlPagePath is not null && File.Exists(result.HtmlPagePath))
            {
                job.HtmlBytes = await File.ReadAllBytesAsync(result.HtmlPagePath);
            }

            // Remove temp directory — we only need the HTML bytes.
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not clean up temp dir {Dir}", tempDir);
            }

            job.Status    = JobStatus.Completed;
            job.Message   = "Done!";
            job.Percent   = 100;
            job.UpdatedAt = DateTime.UtcNow;

            logger.LogInformation("[{JobId}] Completed successfully", job.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{JobId}] Pipeline failed", job.Id);

            job.Status    = JobStatus.Failed;
            job.Message   = "Pipeline failed";
            job.Error     = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;

            // Best-effort cleanup on failure too.
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }
}
