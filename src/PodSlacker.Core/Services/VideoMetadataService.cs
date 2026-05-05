using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace PodSlacker.Core.Services;

/// <summary>
/// Fetches YouTube video metadata (title, video ID) and resolves video stream URLs.
///
/// Title fetch strategy (same priority as Python):
///   1. YouTube oEmbed API — fast, no authentication needed.
///   2. YoutubeExplode — uses YouTube's internal player APIs natively in .NET.
///
/// Stream URL resolution uses YoutubeExplode, which negotiates the best available
/// stream from YouTube's internal manifest — no external binary required.
/// </summary>
public sealed class VideoMetadataService(ILogger<VideoMetadataService> logger)
{
    private const string OEmbedUrl = "https://www.youtube.com/oembed?url={0}&format=json";

    // ── Video ID ────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the 11-character video ID from any standard YouTube URL format
    /// (<c>watch?v=</c>, <c>youtu.be/</c>, <c>/embed/</c>, <c>/shorts/</c>).
    /// </summary>
    /// <param name="url">The YouTube video URL to parse.</param>
    /// <returns>The 11-character video ID.</returns>
    /// <exception cref="ArgumentException">Thrown when no video ID can be found in <paramref name="url"/>.</exception>
    public static string ExtractVideoId(string url)
    {
        var match = Regex.Match(url,
            @"(?:v=|/v/|youtu\.be/|/embed/|/shorts/)([A-Za-z0-9_-]{11})");
        if (!match.Success)
            throw new ArgumentException($"Could not extract a video ID from URL: {url}");
        return match.Groups[1].Value;
    }

    /// <summary>
    /// Converts a video title into a lowercase, underscore-separated filename slug,
    /// stripping punctuation and truncating to <paramref name="maxLen"/> characters.
    /// </summary>
    /// <param name="title">The raw video title.</param>
    /// <param name="maxLen">Maximum number of characters in the returned slug.</param>
    /// <returns>A safe filename slug, or <c>"untitled"</c> if the result would be empty.</returns>
    public static string SanitizeTitle(string title, int maxLen = 50)
    {
        string slug = title.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^\w\s-]", "");
        slug = Regex.Replace(slug, @"[\s\-]+", "_");
        slug = slug.Trim('_');
        if (slug.Length > maxLen)
            slug = slug[..maxLen].TrimEnd('_');
        return string.IsNullOrEmpty(slug) ? "untitled" : slug;
    }

    // ── Title fetch ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the human-readable title for a YouTube video, trying the oEmbed API
    /// first and falling back to YoutubeExplode's video metadata API.
    /// </summary>
    /// <param name="url">The YouTube video URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The video title, or <see langword="null"/> if both strategies fail.</returns>
    public async Task<string?> FetchTitleAsync(string url, CancellationToken ct = default)
    {
        // Method 1: oEmbed — fast, no API key
        try
        {
            using var http  = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.GetAsync(string.Format(OEmbedUrl, Uri.EscapeDataString(url)), ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("title", out var titleProp))
            {
                string? t = titleProp.GetString();
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("oEmbed title fetch failed ({Type}): {Msg} — trying YoutubeExplode",
                ex.GetType().Name, ex.Message);
        }

        // Method 2: YoutubeExplode — uses internal YouTube player API
        try
        {
            var youtube = new YoutubeClient();
            var video   = await youtube.Videos.GetAsync(url, ct);
            if (!string.IsNullOrWhiteSpace(video.Title)) return video.Title;
        }
        catch (Exception ex)
        {
            logger.LogWarning("YoutubeExplode title fetch failed ({Type}): {Msg}",
                ex.GetType().Name, ex.Message);
        }

        return null;
    }

    // ── Stream URL ──────────────────────────────────────────────────────────

    /// <summary>
    /// Uses YoutubeExplode to resolve the best H.264 ≤720p stream URL suitable
    /// for OpenCV frame capture. Prefers MP4 (H.264/AVC) containers at or below
    /// 720p; falls back to the highest-quality video-only stream if no match is found.
    /// </summary>
    /// <param name="videoUrl">The YouTube video URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A direct HTTP URL to the video stream, valid for several hours.</returns>
    public async Task<string> GetStreamUrlAsync(string videoUrl, CancellationToken ct = default)
    {
        var youtube  = new YoutubeClient();
        var manifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl, ct);

        // Priority 1: MP4 (H.264/AVC) at or below 720p — mirrors yt-dlp format string
        //   bestvideo[height<=720][vcodec^=avc1]/bestvideo[vcodec^=avc1]/…
        var mp4Streams = manifest
            .GetVideoOnlyStreams()
            .Where(s => s.Container == Container.Mp4)
            .ToList();

        if (mp4Streams.Count > 0)
        {
            // Prefer ≤720p; if none, take highest regardless of resolution.
            var candidates = mp4Streams
                .Where(s => s.VideoResolution.Height <= 720)
                .ToList();

            var chosen = candidates.Count > 0
                ? candidates.GetWithHighestVideoQuality()
                : mp4Streams.GetWithHighestVideoQuality();

            logger.LogInformation(
                "Stream resolved: {Label} {Container} ({Bitrate:F0} kbps)",
                chosen.VideoQuality.Label, chosen.Container, chosen.Bitrate.KiloBitsPerSecond);

            return chosen.Url;
        }

        // Priority 2: Any video-only stream (WebM/VP9 or other container)
        var allVideoStreams = manifest.GetVideoOnlyStreams().ToList();
        if (allVideoStreams.Count == 0)
            throw new InvalidOperationException(
                $"No video-only streams found for {videoUrl}. " +
                "The video may be restricted or unavailable in your region.");

        var fallback = allVideoStreams
            .Where(s => s.VideoResolution.Height <= 720)
            .ToList();

        var fallbackChosen = fallback.Count > 0
            ? fallback.GetWithHighestVideoQuality()
            : allVideoStreams.GetWithHighestVideoQuality();

        logger.LogInformation(
            "Stream resolved (fallback): {Label} {Container} ({Bitrate:F0} kbps)",
            fallbackChosen.VideoQuality.Label, fallbackChosen.Container,
            fallbackChosen.Bitrate.KiloBitsPerSecond);

        return fallbackChosen.Url;
    }
}
