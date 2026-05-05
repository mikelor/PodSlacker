using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PodSlacker.Core.Models;
using YoutubeExplode;

namespace PodSlacker.Core.Services;

/// <summary>
/// Fetches YouTube transcripts using a four-tier strategy, trying each in order:
///
///   1. <b>YoutubeExplode</b> — the primary strategy. Uses YoutubeExplode's closed-caption
///      API which reverse-engineers YouTube's internal player APIs (the same mechanisms
///      used by yt-dlp) but natively in .NET — no external binary required. Handles
///      YouTube's Proof-of-Origin Token (POT) internally and prefers manually
///      uploaded captions over auto-generated ones.
///
///   2. <b>Watch-page / ytInitialPlayerResponse</b> — parses the JSON object
///      YouTube embeds in every page and uses the embedded caption URL. Works
///      without an API key but may fail if YouTube adds a POT to the URL.
///
///   3. <b>YouTubei v1/player API</b> — the internal JSON API with the public
///      WEB client key and required headers.
///
///   4. <b>Regex + timedtext XML</b> — legacy fallback that scrapes the watch
///      page for a bare captionTracks URL and fetches the timedtext XML.
///
/// Cookie and proxy support is available in all tiers.
/// </summary>
public sealed class TranscriptService(ILogger<TranscriptService> logger)
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the English transcript for the specified YouTube video, trying up to
    /// four strategies in order of reliability (YoutubeExplode → watch-page JSON →
    /// YouTubei v1/player API → timedtext XML).
    /// </summary>
    /// <param name="videoId">The 11-character YouTube video ID.</param>
    /// <param name="cookiesFilePath">
    /// Optional path to a Netscape-format cookies.txt file. Loaded into the HTTP client
    /// for the YoutubeExplode tier and all HTTP fallback tiers, useful for accessing
    /// age-restricted or member-only videos.
    /// </param>
    /// <param name="proxyUrl">Optional proxy URL forwarded to all HTTP clients.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple containing the full concatenated transcript text and a list of
    /// timed entries (each pairing a start offset in seconds with its caption text).
    /// </returns>
    public async Task<(string FullText, List<TimedTranscriptEntry> TimedEntries)> FetchAsync(
        string  videoId,
        string? cookiesFilePath = null,
        string? proxyUrl        = null,
        CancellationToken ct    = default)
    {
        // ── Tier 1: YoutubeExplode ───────────────────────────────────────────
        try
        {
            logger.LogInformation("Fetching transcript via YoutubeExplode for {VideoId}", videoId);
            return await FetchViaYoutubeExplodeAsync(videoId, cookiesFilePath, proxyUrl, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YoutubeExplode transcript fetch failed, trying watch-page strategy");
        }

        using var handler = BuildHandler(cookiesFilePath, proxyUrl);
        using var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent",      UserAgent);
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

        // ── Tier 2: watch-page ytInitialPlayerResponse ───────────────────────
        try
        {
            logger.LogInformation("Fetching transcript via watch-page for {VideoId}", videoId);
            return await FetchViaWatchPageAsync(client, videoId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Watch-page strategy failed, trying YouTubei API");
        }

        // ── Tier 3: YouTubei v1/player API ───────────────────────────────────
        try
        {
            return await FetchViaInternalApiAsync(client, videoId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTubei API failed, trying timedtext XML fallback");
        }

        // ── Tier 4: regex + timedtext XML ─────────────────────────────────────
        return await FetchViaTimedTextAsync(client, videoId, ct);
    }

    // ── Tier 1: YoutubeExplode ───────────────────────────────────────────────

    private async Task<(string, List<TimedTranscriptEntry>)> FetchViaYoutubeExplodeAsync(
        string videoId, string? cookiesFilePath, string? proxyUrl, CancellationToken ct)
    {
        var youtube = CreateYoutubeClient(cookiesFilePath, proxyUrl);
        string videoUrl = $"https://www.youtube.com/watch?v={videoId}";

        var manifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoUrl, ct);

        if (manifest.Tracks.Count == 0)
            throw new InvalidOperationException(
                $"YoutubeExplode found no caption tracks for '{videoId}'.");

        // Prefer manually uploaded English track; fall back to auto-generated.
        var trackInfo = manifest.Tracks
            .Where(t => t.Language.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.IsAutoGenerated)   // false (manual) sorts before true (auto)
            .FirstOrDefault()
            ?? manifest.Tracks.First();         // any language if no English track exists

        string trackKind = trackInfo.IsAutoGenerated ? "auto-generated" : "manual";
        logger.LogInformation("Downloading {Kind} captions [{Lang}] via YoutubeExplode",
            trackKind, trackInfo.Language.Code);

        var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo, ct);

        var entries = new List<TimedTranscriptEntry>(track.Captions.Count);
        var sb      = new StringBuilder(track.Captions.Count * 60);

        foreach (var caption in track.Captions)
        {
            // Text may contain newlines from multi-part captions — normalise to spaces.
            string text = caption.Text
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();

            if (string.IsNullOrWhiteSpace(text)) continue;

            entries.Add(new TimedTranscriptEntry(caption.Offset.TotalSeconds, text));
            sb.Append(text).Append(' ');
        }

        if (entries.Count == 0)
            throw new InvalidOperationException(
                "YoutubeExplode returned a caption track with zero usable entries.");

        return (sb.ToString().Trim(), entries);
    }

    // ── Tier 2: watch-page ───────────────────────────────────────────────────

    private async Task<(string, List<TimedTranscriptEntry>)> FetchViaWatchPageAsync(
        HttpClient client, string videoId, CancellationToken ct)
    {
        string url  = $"https://www.youtube.com/watch?v={videoId}";
        var    resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync(ct);

        if (html.Contains("consent.youtube.com", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "YouTube served a consent page. Pass --cookies with exported browser cookies.");

        string marker = "ytInitialPlayerResponse";
        int    mIdx   = html.IndexOf(marker, StringComparison.Ordinal);
        if (mIdx < 0)
            throw new InvalidOperationException("ytInitialPlayerResponse not found in page.");

        int braceStart = html.IndexOf('{', mIdx + marker.Length);
        if (braceStart < 0)
            throw new InvalidOperationException("Could not locate JSON start for ytInitialPlayerResponse.");

        string? json = ExtractJsonObject(html, braceStart);
        if (json is null)
            throw new InvalidOperationException("Could not extract balanced JSON from ytInitialPlayerResponse.");

        using var doc      = JsonDocument.Parse(json);
        string    capUrl   = SelectCaptionUrl(doc.RootElement, videoId);
        var       capResp  = await client.GetAsync(capUrl, ct);
        capResp.EnsureSuccessStatusCode();

        string body = await capResp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException(
                "Caption URL returned an empty response. " +
                "YouTube's POT (Proof-of-Origin Token) may be required for this video.");

        // Detect format: JSON3 starts with '{', timedtext XML starts with '<'
        if (body.TrimStart().StartsWith('<'))
            return ParseTimedTextXml(body);

        using var capDoc = JsonDocument.Parse(body);
        return ParseJson3Transcript(capDoc);
    }

    // ── Tier 3: YouTubei v1/player ────────────────────────────────────────────

    private async Task<(string, List<TimedTranscriptEntry>)> FetchViaInternalApiAsync(
        HttpClient client, string videoId, CancellationToken ct)
    {
        const string ApiKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";

        var payload = new
        {
            videoId,
            context = new
            {
                client = new
                {
                    clientName    = "WEB",
                    clientVersion = "2.20240101.00.00",
                    hl = "en",
                    gl = "US",
                }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://www.youtube.com/youtubei/v1/player?key={ApiKey}&prettyPrint=false")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-YouTube-Client-Name",    "1");
        req.Headers.Add("X-YouTube-Client-Version", "2.20240101.00.00");
        req.Headers.Add("Origin",  "https://www.youtube.com");
        req.Headers.Add("Referer", "https://www.youtube.com/");

        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc    = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        string    capUrl = SelectCaptionUrl(doc.RootElement, videoId);

        var capResp = await client.GetAsync(capUrl, ct);
        capResp.EnsureSuccessStatusCode();
        string body = await capResp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Caption URL returned an empty response (POT required).");

        if (body.TrimStart().StartsWith('<'))
            return ParseTimedTextXml(body);

        using var capDoc = JsonDocument.Parse(body);
        return ParseJson3Transcript(capDoc);
    }

    // ── Tier 4: regex + timedtext XML ─────────────────────────────────────────

    private async Task<(string, List<TimedTranscriptEntry>)> FetchViaTimedTextAsync(
        HttpClient client, string videoId, CancellationToken ct)
    {
        var pageResp = await client.GetAsync($"https://www.youtube.com/watch?v={videoId}", ct);
        pageResp.EnsureSuccessStatusCode();
        string html = await pageResp.Content.ReadAsStringAsync(ct);

        var match = Regex.Match(html, @"""captionTracks""\s*:\s*\[.*?""baseUrl""\s*:\s*""(.*?)""",
                                RegexOptions.Singleline);
        if (!match.Success)
            throw new InvalidOperationException(
                "Could not find caption track URL in page source. " +
                "The video may not have captions or may require authentication.");

        string timedTextUrl = Unescape(match.Groups[1].Value);
        var    xmlResp      = await client.GetAsync(timedTextUrl, ct);
        xmlResp.EnsureSuccessStatusCode();
        string body = await xmlResp.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException(
                "Timedtext URL returned an empty response (all strategies exhausted).");

        // Auto-detect format.
        if (body.TrimStart().StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(body);
            return ParseJson3Transcript(doc);
        }
        return ParseTimedTextXml(body);
    }

    // ── Shared parsers ───────────────────────────────────────────────────────

    private static string SelectCaptionUrl(JsonElement root, string videoId)
    {
        if (!root.TryGetProperty("captions", out var captions) ||
            !captions.TryGetProperty("playerCaptionsTracklistRenderer", out var renderer) ||
            !renderer.TryGetProperty("captionTracks", out var tracks) ||
            tracks.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"No caption tracks in player response for '{videoId}'.");
        }

        JsonElement? best = null;
        foreach (var track in tracks.EnumerateArray())
        {
            string? lang  = track.TryGetProperty("languageCode", out var lc) ? lc.GetString() : null;
            bool    isEn  = lang == "en";
            bool    isAsr = track.TryGetProperty("kind", out var k) &&
                            k.GetString()?.Equals("asr", StringComparison.OrdinalIgnoreCase) == true;
            if (best is null) { best = track; continue; }
            if (isEn && !isAsr) { best = track; break; }   // manual EN wins immediately
            if (isEn)           { best = track; }           // auto EN > anything else
        }

        if (!best!.Value.TryGetProperty("baseUrl", out var baseUrlEl))
            throw new InvalidOperationException("Chosen caption track has no baseUrl.");

        // Append fmt=json3. If the URL already has a fmt parameter it is overridden
        // by the last occurrence — harmless.
        return Unescape(baseUrlEl.GetString()!) + "&fmt=json3";
    }

    private static (string, List<TimedTranscriptEntry>) ParseJson3Transcript(JsonDocument doc)
    {
        var entries = new List<TimedTranscriptEntry>();
        var sb      = new StringBuilder();

        if (!doc.RootElement.TryGetProperty("events", out var events))
            throw new InvalidOperationException("No 'events' array in json3 transcript.");

        foreach (var ev in events.EnumerateArray())
        {
            if (!ev.TryGetProperty("segs", out var segs)) continue;
            double start = ev.TryGetProperty("tStartMs", out var ts) ? ts.GetDouble() / 1000.0 : 0;

            var segText = new StringBuilder();
            foreach (var seg in segs.EnumerateArray())
                if (seg.TryGetProperty("utf8", out var utf8))
                    segText.Append(utf8.GetString());

            string text = segText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            entries.Add(new TimedTranscriptEntry(start, text));
            sb.Append(text).Append(' ');
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("Transcript parsed zero entries from json3.");

        return (sb.ToString().Trim(), entries);
    }

    private static (string, List<TimedTranscriptEntry>) ParseTimedTextXml(string xml)
    {
        var entries = new List<TimedTranscriptEntry>();
        var sb      = new StringBuilder();

        // Standard timedtext v1: <text start="12.34" dur="2.5">content</text>
        foreach (Match m in Regex.Matches(xml,
            @"<text\s+start=""([^""]+)""[^>]*>(.*?)</text>", RegexOptions.Singleline))
        {
            double start = double.Parse(m.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            string raw = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            entries.Add(new TimedTranscriptEntry(start, raw));
            sb.Append(raw).Append(' ');
        }

        if (entries.Count == 0)
            throw new InvalidOperationException(
                "Parsed zero entries from timedtext XML. " +
                "The video may use a format not supported by the XML fallback.");

        return (sb.ToString().Trim(), entries);
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────

    private static string? ExtractJsonObject(string text, int start)
    {
        int  depth    = 0;
        bool inString = false;
        bool escape   = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (escape)           { escape = false; continue; }
            if (c == '\\' && inString) { escape = true;  continue; }
            if (c == '"')         inString = !inString;
            if (!inString)
            {
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                    return text[start..(i + 1)];
            }
        }
        return null;
    }

    private static string Unescape(string s) =>
        Regex.Replace(s, @"\\u([0-9a-fA-F]{4})",
            m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString())
        .Replace("&amp;", "&");

    // ── HTTP / YoutubeExplode client factories ────────────────────────────────

    /// <summary>
    /// Creates a <see cref="YoutubeClient"/> configured with optional proxy and
    /// Netscape-format cookie support.
    /// </summary>
    private static YoutubeClient CreateYoutubeClient(string? cookiesFilePath, string? proxyUrl)
    {
        if (cookiesFilePath is null && proxyUrl is null)
            return new YoutubeClient();

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (proxyUrl is not null)
            handler.Proxy = new WebProxy(proxyUrl, false);
        if (cookiesFilePath is not null)
        {
            handler.CookieContainer = LoadNetscapeCookies(cookiesFilePath);
            handler.UseCookies      = true;
        }

        return new YoutubeClient(new HttpClient(handler));
    }

    private static HttpClientHandler BuildHandler(string? cookiesFilePath, string? proxyUrl)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (proxyUrl is not null) handler.Proxy = new WebProxy(proxyUrl, false);
        if (cookiesFilePath is not null)
        {
            handler.CookieContainer = LoadNetscapeCookies(cookiesFilePath);
            handler.UseCookies      = true;
        }
        return handler;
    }

    private static CookieContainer LoadNetscapeCookies(string path)
    {
        var container = new CookieContainer();
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length < 7) continue;
            string domain = parts[0].TrimStart('.');
            bool   secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            try { container.Add(new Cookie(parts[5], parts[6], "/", domain) { Secure = secure }); }
            catch { /* skip malformed */ }
        }
        return container;
    }
}
