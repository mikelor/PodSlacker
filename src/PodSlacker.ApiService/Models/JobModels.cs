using PodSlacker.Core.Models;

namespace PodSlacker.ApiService.Models;

/// <summary>
/// Status values for a PodSlacker job.
/// </summary>
public enum JobStatus
{
    /// <summary>Job is queued but has not started yet.</summary>
    Queued,
    /// <summary>Pipeline is actively running.</summary>
    Running,
    /// <summary>Pipeline completed successfully; HTML page is ready to download.</summary>
    Completed,
    /// <summary>Pipeline failed with an unrecoverable error.</summary>
    Failed,
}

/// <summary>
/// In-memory record for a single async PodSlacker job.
/// </summary>
public sealed class JobRecord
{
    /// <summary>Unique job identifier (UUID string).</summary>
    public string     Id          { get; init; } = Guid.NewGuid().ToString();
    /// <summary>YouTube URL submitted with the job.</summary>
    public string     VideoUrl    { get; init; } = string.Empty;
    /// <summary>Video title; populated once the pipeline fetches it from YouTube.</summary>
    public string?    Title       { get; set; }
    // ── User-selected generation parameters ──────────────────────────────────
    /// <summary>1 = monologue, 2 = dialogue.</summary>
    public int        Hosts       { get; init; } = 2;
    /// <summary>Display name for the first (or sole) host.</summary>
    public string     Host1Name   { get; init; } = "JORDAN";
    /// <summary>Display name for the second host; only used when <see cref="Hosts"/> is 2.</summary>
    public string     Host2Name   { get; init; } = "MIKE";
    /// <summary>LLM model ID used for this job.</summary>
    public string     LlmModel    { get; init; } = string.Empty;
    /// <summary>Number of key-moment frames requested.</summary>
    public int        NumFrames   { get; init; } = 6;
    /// <summary>Whether GitHub Pages publishing was requested.</summary>
    public bool       PublishGitHub { get; init; }
    /// <summary>Optional subfolder in the gh-pages repo where files were published.</summary>
    public string     GithubFolder  { get; init; } = "";
    /// <summary>Current pipeline status.</summary>
    public JobStatus  Status      { get; set; }  = JobStatus.Queued;
    /// <summary>Most recent progress message from the pipeline.</summary>
    public string     Message     { get; set; }  = "Queued";
    /// <summary>Overall progress percentage (0–100).</summary>
    public int        Percent     { get; set; }
    /// <summary>Error message if <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</summary>
    public string?    Error       { get; set; }
    /// <summary>Raw HTML bytes of the generated page; available once status is Completed.</summary>
    public byte[]?    HtmlBytes      { get; set; }
    /// <summary>Public GitHub Pages URL; set only when the job was submitted with PublishGithub=true and publishing succeeded.</summary>
    public string?    GitHubPagesUrl { get; set; }
    /// <summary>UTC time this record was created.</summary>
    public DateTime   CreatedAt   { get; init; } = DateTime.UtcNow;
    /// <summary>UTC time the pipeline last reported progress.</summary>
    public DateTime   UpdatedAt   { get; set; }  = DateTime.UtcNow;
}

/// <summary>
/// Request body sent by the CLI when submitting a new job.
/// The full <see cref="PodSlackerConfig"/> is included so the server uses
/// the caller's settings rather than any server-side defaults.
/// </summary>
public sealed class CreateJobRequest
{
    /// <summary>YouTube video URL to process.</summary>
    public required string           VideoUrl { get; init; }
    /// <summary>
    /// Full runtime configuration. The server will override
    /// <see cref="PodSlackerConfig.OutputDir"/> with a temporary directory
    /// and will ignore <see cref="PodSlackerConfig.PublishGithub"/> (the API
    /// returns the HTML directly).
    /// </summary>
    public PodSlackerConfig Config   { get; init; } = new();
}

/// <summary>
/// Lightweight status DTO returned by <c>GET /api/jobs/{id}</c>.
/// </summary>
public sealed class JobStatusResponse
{
    /// <summary>Job identifier.</summary>
    public string     Id         { get; init; } = string.Empty;
    /// <summary>YouTube URL.</summary>
    public string     VideoUrl   { get; init; } = string.Empty;
    /// <summary>Video title; available once the pipeline has fetched it.</summary>
    public string?    Title      { get; init; }
    /// <summary>Current status.</summary>
    public JobStatus  Status     { get; init; }
    /// <summary>Latest progress message.</summary>
    public string     Message    { get; init; } = string.Empty;
    /// <summary>Progress percentage (0–100).</summary>
    public int        Percent    { get; init; }
    /// <summary>Error description when <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</summary>
    public string?    Error      { get; init; }
    /// <summary>
    /// Download URL for the generated HTML page.
    /// Only present when <see cref="Status"/> is <see cref="JobStatus.Completed"/>.
    /// </summary>
    public string?    PageUrl        { get; init; }
    /// <summary>
    /// Public GitHub Pages URL for the published page.
    /// Only present when the job was submitted with <c>publish_github: true</c> and publishing succeeded.
    /// </summary>
    public string?    GitHubPagesUrl { get; init; }
    // ── User-selected generation parameters ──────────────────────────────────
    /// <summary>1 = monologue, 2 = dialogue.</summary>
    public int        Hosts         { get; init; } = 2;
    /// <summary>Display name for the first (or sole) host.</summary>
    public string     Host1Name     { get; init; } = "JORDAN";
    /// <summary>Display name for the second host; only relevant when <see cref="Hosts"/> is 2.</summary>
    public string     Host2Name     { get; init; } = "MIKE";
    /// <summary>LLM model ID used for this job.</summary>
    public string     LlmModel      { get; init; } = string.Empty;
    /// <summary>Number of key-moment frames requested.</summary>
    public int        NumFrames     { get; init; } = 6;
    /// <summary>Whether GitHub Pages publishing was requested.</summary>
    public bool       PublishGitHub { get; init; }
    /// <summary>Optional subfolder in the gh-pages repo where files were published.</summary>
    public string     GithubFolder  { get; init; } = "";
}
