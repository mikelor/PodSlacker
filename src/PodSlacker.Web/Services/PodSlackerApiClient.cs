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
    public async Task<string> SubmitJobAsync(string videoUrl, CancellationToken ct = default)
    {
        var body = new { video_url = videoUrl };
        var response = await http.PostAsJsonAsync("api/jobs", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from API service.");

        return status.Id;
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
