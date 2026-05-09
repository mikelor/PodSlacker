using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PodSlacker.Web.Services;

/// <summary>DTO for a job status response from the API service.</summary>
public sealed class JobStatusDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("video_url")]
    public string VideoUrl { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("percent")]
    public int Percent { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("page_url")]
    public string? PageUrl { get; init; }

    [JsonPropertyName("git_hub_pages_url")]
    public string? GitHubPagesUrl { get; init; }

    // ── User-selected generation parameters ──────────────────────────────────
    [JsonPropertyName("hosts")]
    public int Hosts { get; init; } = 2;

    /// <summary>Display name for the first (or sole) host.</summary>
    [JsonPropertyName("host1_name")]
    public string Host1Name { get; init; } = "JORDAN";

    /// <summary>Display name for the second host; only relevant when <see cref="Hosts"/> is 2.</summary>
    [JsonPropertyName("host2_name")]
    public string Host2Name { get; init; } = "MIKE";

    [JsonPropertyName("llm_model")]
    public string LlmModel { get; init; } = string.Empty;

    [JsonPropertyName("num_frames")]
    public int NumFrames { get; init; } = 6;

    [JsonPropertyName("publish_git_hub")]
    public bool PublishGitHub { get; init; }

    [JsonPropertyName("github_folder")]
    public string GithubFolder { get; init; } = "";
}

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for the PodSlacker API service.
/// In Aspire mode the base address is resolved via service discovery
/// ("http://podslacker-api"). In manual mode it is read from
/// <c>PODSLACKER_SERVICE_URL</c> or <c>appsettings.json</c>.
/// </summary>
public sealed class PodSlackerApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Submits a new job and returns the assigned job ID.</summary>
    /// <param name="videoUrl">YouTube video URL.</param>
    /// <param name="hosts">1 for a solo monologue, 2 for a two-host dialogue (default).</param>
    /// <param name="host1Name">Display name for the first (or sole) host.</param>
    /// <param name="host2Name">Display name for the second host; ignored when <paramref name="hosts"/> is 1.</param>
    /// <param name="numFrames">Number of key-moment frames to capture (0 = none, default 6).</param>
    /// <param name="llmModel">OpenRouter model ID to use for all LLM steps.</param>
    /// <param name="publishGitHub">When true, publish the finished page to GitHub Pages.</param>
    /// <param name="githubRepo">GitHub repository name to publish to.</param>
    /// <param name="githubTokenValue">Literal PAT; overrides server GITHUB_TOKEN env var when set.</param>
    /// <param name="githubFolder">Optional subfolder in the gh-pages repo where files are published (e.g. "google-cloud-next").</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> SubmitJobAsync(
        string  videoUrl,
        int     hosts            = 2,
        string  host1Name        = "JORDAN",
        string  host2Name        = "MIKE",
        int     numFrames        = 6,
        string  llmModel         = "openrouter/auto:free",
        bool    publishGitHub    = false,
        string  githubRepo       = "podslacker-pages",
        string? githubTokenValue = null,
        string  githubFolder     = "",
        CancellationToken ct     = default)
    {
        var body = new
        {
            video_url = videoUrl,
            config = new
            {
                hosts,
                host1_name         = host1Name,
                host2_name         = host2Name,
                num_frames         = numFrames,
                llm_model          = llmModel,
                llm_base_url       = "https://openrouter.ai/api/v1",
                publish_github     = publishGitHub,
                github_repo        = githubRepo,
                github_token_value = githubTokenValue,
                github_folder      = githubFolder,
            },
        };
        var response = await http.PostAsJsonAsync("api/jobs", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from API service.");

        return status.Id;
    }

    /// <summary>Returns all in-memory jobs, newest first.</summary>
    public async Task<List<JobStatusDto>> GetAllJobsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/jobs", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobStatusDto>>(JsonOptions, ct)
            ?? [];
    }

    /// <summary>Returns the current status of a job.</summary>
    public async Task<JobStatusDto> GetJobStatusAsync(string jobId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/jobs/{jobId}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from API service.");
    }

    /// <summary>Downloads the generated HTML page bytes for a completed job.</summary>
    public async Task<byte[]> DownloadPageAsync(string jobId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/jobs/{jobId}/page", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
