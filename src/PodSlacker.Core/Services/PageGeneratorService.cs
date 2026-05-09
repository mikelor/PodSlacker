using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using PodSlacker.Core.Audio;

namespace PodSlacker.Core.Services;

/// <summary>
/// The result of <see cref="PageGeneratorService.GeneratePage"/>.
/// </summary>
/// <param name="HtmlPath">Absolute path to the generated HTML file.</param>
/// <param name="AssetPaths">
/// Absolute paths of asset files (audio, images) that must be published
/// alongside the HTML when <c>embedAssets</c> is <see langword="false"/>.
/// Empty when assets are embedded.
/// </param>
public sealed record PageGeneratorResult(string HtmlPath, IReadOnlyList<string> AssetPaths);

/// <summary>
/// Generates an HTML page for the podcast episode and writes it to disk.
/// Assets (audio, images) can be embedded as base64 data URIs (default) or
/// referenced via relative paths for GitHub Pages / multi-file publishing.
/// </summary>
public static class PageGeneratorService
{
    private static readonly MarkdownPipeline MdPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates an HTML page for the podcast episode and writes it to disk.
    /// </summary>
    /// <param name="videoId">YouTube video ID used to build the output filename and YouTube deep-link URLs.</param>
    /// <param name="url">Source YouTube URL displayed in the page header.</param>
    /// <param name="outputDir">Directory where the HTML file will be written.</param>
    /// <param name="audioPath">Optional path to the audio file.</param>
    /// <param name="imagePaths">Optional list of JPEG frame image paths.</param>
    /// <param name="mdPath">Optional path to the markdown summary file to render as HTML prose.</param>
    /// <param name="title">Optional video title displayed in the page header.</param>
    /// <param name="baseName">
    /// Base name for the output file (without extension).
    /// Defaults to <paramref name="videoId"/> when <see langword="null"/>.
    /// </param>
    /// <param name="transcriptPath">Optional path to the timestamped transcript text file shown in the collapsible section.</param>
    /// <param name="frameCaptions">Optional captions aligned by index with <paramref name="imagePaths"/>.</param>
    /// <param name="embedAssets">
    /// When <see langword="true"/> (default), audio and images are base64-encoded directly
    /// into the HTML so the page works as a single self-contained file.
    /// When <see langword="false"/>, the HTML references assets via relative paths — suitable
    /// for GitHub Pages where audio and images are uploaded as separate files.
    /// </param>
    /// <param name="githubPagesIndexUrl">
    /// When non-<see langword="null"/>, a "← Back to Index" pill linking to this URL is
    /// rendered to the left of the "Watch on YouTube" pill in the page header.
    /// Typically the root URL of the GitHub Pages site (e.g.
    /// <c>https://username.github.io/podslacker-pages/</c>).
    /// </param>
    /// <returns>
    /// A <see cref="PageGeneratorResult"/> containing the path to the written HTML file and,
    /// when <paramref name="embedAssets"/> is <see langword="false"/>, the list of asset
    /// file paths (audio + images) that must be published alongside the HTML.
    /// </returns>
    public static PageGeneratorResult GeneratePage(
        string              videoId,
        string              url,
        string              outputDir,
        string?             audioPath           = null,
        List<string>?       imagePaths          = null,
        string?             mdPath              = null,
        string?             title               = null,
        string?             baseName            = null,
        string?             transcriptPath      = null,
        List<string>?       frameCaptions       = null,
        bool                embedAssets         = true,
        string?             githubPagesIndexUrl = null)
    {
        var    assets     = new List<string>();  // populated only when embedAssets=false
        string summaryHtml = BuildSummaryHtml(mdPath);
        var (audioElement, audioFname, hasAudio) =
            embedAssets
                ? BuildAudioElementEmbedded(audioPath)
                : BuildAudioElementReferenced(audioPath, assets);
        string gallerySection    = embedAssets
            ? BuildGallerySectionEmbedded(imagePaths, frameCaptions)
            : BuildGallerySectionReferenced(imagePaths, frameCaptions, assets);
        string transcriptSection = embedAssets
            ? BuildTranscriptSectionEmbedded(transcriptPath, videoId)
            : BuildTranscriptSectionReferenced(transcriptPath, assets);
        string playerBlock       = BuildPlayerBlock(hasAudio, audioElement, audioFname);

        string urlEsc       = HtmlEncode(url);
        string vidEsc       = HtmlEncode(videoId);
        string? titleEsc    = title is not null ? HtmlEncode(title) : null;
        string bodyAttrs    = hasAudio ? " class=\"has-player\"" : "";
        string psLink       = "<a href=\"https://podslacker.com\" target=\"_blank\" rel=\"noopener\">PodSlacker</a>";
        string pageTitleTag = $"PodSlacker — {titleEsc ?? vidEsc}";
        string headerH1     = titleEsc ?? $"Created with {psLink}";
        string headerSub    = titleEsc is not null
            ? $"  <div class=\"vid-badge\">Created with {psLink} &nbsp;·&nbsp; {vidEsc}</div><br>\n"
            : $"  <div class=\"vid-badge\">{vidEsc}</div><br>\n";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"UTF-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
        sb.Append($"<title>{pageTitleTag}</title>\n");
        sb.Append("<style>\n").Append(PageCss).Append("\n</style>\n</head>\n");
        sb.Append($"<body{bodyAttrs}>\n\n");
        sb.Append("<div class=\"ps-cta-bar\">\n");
        sb.Append("  <img src=\"https://podslacker.com/logo.png\" alt=\"PodSlacker\" class=\"ps-cta-logo\" onerror=\"this.style.display='none'\" />\n");
        sb.Append("  <span>This SlackCast was generated by <strong>PodSlacker</strong> &mdash; turn any YouTube video into a podcast.</span>\n");
        sb.Append("  <a href=\"https://podslacker.com\" target=\"_blank\" rel=\"noopener\" class=\"ps-cta-btn\">Try PodSlacker now &#8599;</a>\n");
        sb.Append("</div>\n\n");
        sb.Append("<header class=\"page-header\">\n");
        sb.Append($"  <h1>{headerH1}</h1>\n");
        sb.Append(headerSub);
        sb.Append("  <div class=\"header-links\">\n");
        if (githubPagesIndexUrl is not null)
        {
            string indexUrlEsc = HtmlEncode(githubPagesIndexUrl);
            sb.Append($"  <a class=\"source-link\" href=\"{indexUrlEsc}\" target=\"_blank\" rel=\"noopener\">&#8592; Back to Index</a>\n");
        }
        sb.Append($"  <a class=\"source-link\" href=\"{urlEsc}\" target=\"_blank\" rel=\"noopener\">Watch on YouTube &#8599;</a>\n");
        sb.Append("  </div>\n");
        sb.Append("</header>\n\n");
        sb.Append("<div class=\"content\">\n");
        sb.Append(gallerySection);
        sb.Append("\n<section class=\"section\">\n");
        sb.Append("<h2 class=\"section-heading\">Summary &amp; Script</h2>\n");
        sb.Append($"<div class=\"prose\">{summaryHtml}</div>\n");
        sb.Append("</section>");
        sb.Append(transcriptSection);
        sb.Append("\n</div>\n");
        sb.Append(playerBlock);
        sb.Append("\n\n<div id=\"lightbox\" class=\"lightbox\" role=\"dialog\" aria-modal=\"true\">");
        sb.Append("<span class=\"lightbox-close\" aria-label=\"Close\">&times;</span>");
        sb.Append("<div class=\"lightbox-content\">");
        sb.Append("<img id=\"lightboxImg\" src=\"\" alt=\"Full size frame\">");
        sb.Append("<p id=\"lightboxCaption\" class=\"lightbox-caption\"></p>");
        sb.Append("</div></div>");
        sb.Append("\n\n<script>\n").Append(PageJs).Append("</script>\n\n</body>\n</html>\n");

        string pagePath = Path.Combine(outputDir, $"{baseName ?? videoId}_page.html");
        File.WriteAllText(pagePath, sb.ToString(), Encoding.UTF8);
        return new PageGeneratorResult(pagePath, assets);
    }

    // ── Section builders ─────────────────────────────────────────────────────

    private static string BuildSummaryHtml(string? mdPath)
    {
        if (mdPath is null || !File.Exists(mdPath)) return string.Empty;
        string raw = File.ReadAllText(mdPath, Encoding.UTF8);
        return Markdown.ToHtml(raw, MdPipeline);
    }

    // Embedded (base64) audio — assets list unchanged.
    private static (string Element, string Fname, bool HasAudio)
        BuildAudioElementEmbedded(string? audioPath)
    {
        if (audioPath is null || !File.Exists(audioPath))
            return (string.Empty, string.Empty, false);

        byte[] audioBytes = File.ReadAllBytes(audioPath);

        if (Path.GetExtension(audioPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            audioBytes = Mp3FrameParser.StripVbrInfoFrames(audioBytes, out int stripped);
            if (stripped > 0)
                Console.WriteLine($"   ℹ️   Stripped {stripped} VBR info frame(s) from MP3 for browser embedding.");
        }

        string mime    = Path.GetExtension(audioPath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? "audio/wav" : "audio/mpeg";
        string b64     = Convert.ToBase64String(audioBytes);
        string fname   = HtmlEncode(Path.GetFileName(audioPath));
        string element = $"<audio id=\"audioEl\" preload=\"metadata\">" +
                         $"<source src=\"data:{mime};base64,{b64}\" type=\"{mime}\"></audio>";

        return (element, fname, true);
    }

    // Referenced audio — emits a relative src path; adds the local path to assets.
    private static (string Element, string Fname, bool HasAudio)
        BuildAudioElementReferenced(string? audioPath, List<string> assets)
    {
        if (audioPath is null || !File.Exists(audioPath))
            return (string.Empty, string.Empty, false);

        string mime  = Path.GetExtension(audioPath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? "audio/wav" : "audio/mpeg";
        string fname = HtmlEncode(Path.GetFileName(audioPath));
        string element = $"<audio id=\"audioEl\" preload=\"metadata\">" +
                         $"<source src=\"{fname}\" type=\"{mime}\"></audio>";

        assets.Add(audioPath);
        return (element, fname, true);
    }

    // Embedded (base64) gallery — images become inline data URIs.
    private static string BuildGallerySectionEmbedded(List<string>? imagePaths, List<string>? captions)
    {
        if (imagePaths is null || imagePaths.Count == 0) return string.Empty;

        var items = new StringBuilder();
        for (int idx = 0; idx < imagePaths.Count; idx++)
        {
            string imgPath = imagePaths[idx];
            if (!File.Exists(imgPath)) continue;

            string b64         = Convert.ToBase64String(File.ReadAllBytes(imgPath));
            string fname       = HtmlEncode(Path.GetFileName(imgPath));
            string captionText = captions is not null && idx < captions.Count ? captions[idx] : string.Empty;
            string captionEsc  = HtmlEncode(captionText);
            string figcaption  = !string.IsNullOrEmpty(captionEsc)
                ? $"<figcaption class=\"gal-caption\">{captionEsc}</figcaption>"
                : $"<figcaption class=\"gal-caption\">{fname}</figcaption>";

            items.Append($"<figure class=\"gal-item\">");
            items.Append($"<img src=\"data:image/jpeg;base64,{b64}\" alt=\"{fname}\"");
            items.Append($" loading=\"lazy\" data-caption=\"{captionEsc}\">");
            items.Append(figcaption);
            items.Append("</figure>\n");
        }

        if (items.Length == 0) return string.Empty;

        return "\n<section class=\"section\">" +
               "\n<h2 class=\"section-heading\">Key Moments</h2>" +
               "\n<div class=\"gallery\">\n" +
               items +
               "\n</div>\n</section>";
    }

    // Referenced gallery — images use relative filename src; paths added to assets list.
    private static string BuildGallerySectionReferenced(
        List<string>? imagePaths, List<string>? captions, List<string> assets)
    {
        if (imagePaths is null || imagePaths.Count == 0) return string.Empty;

        var items = new StringBuilder();
        for (int idx = 0; idx < imagePaths.Count; idx++)
        {
            string imgPath = imagePaths[idx];
            if (!File.Exists(imgPath)) continue;

            string fname       = HtmlEncode(Path.GetFileName(imgPath));
            string captionText = captions is not null && idx < captions.Count ? captions[idx] : string.Empty;
            string captionEsc  = HtmlEncode(captionText);
            string figcaption  = !string.IsNullOrEmpty(captionEsc)
                ? $"<figcaption class=\"gal-caption\">{captionEsc}</figcaption>"
                : $"<figcaption class=\"gal-caption\">{fname}</figcaption>";

            items.Append($"<figure class=\"gal-item\">");
            items.Append($"<img src=\"{fname}\" alt=\"{fname}\"");
            items.Append($" loading=\"lazy\" data-caption=\"{captionEsc}\">");
            items.Append(figcaption);
            items.Append("</figure>\n");

            assets.Add(imgPath);
        }

        if (items.Length == 0) return string.Empty;

        return "\n<section class=\"section\">" +
               "\n<h2 class=\"section-heading\">Key Moments</h2>" +
               "\n<div class=\"gallery\">\n" +
               items +
               "\n</div>\n</section>";
    }

    // Referenced transcript — renders a download link; adds the local path to assets.
    private static string BuildTranscriptSectionReferenced(string? transcriptPath, List<string> assets)
    {
        if (transcriptPath is null || !File.Exists(transcriptPath)) return string.Empty;

        string fname = HtmlEncode(Path.GetFileName(transcriptPath));
        assets.Add(transcriptPath);

        return "\n<section class=\"section\">" +
               "\n<h2 class=\"section-heading\">Transcript</h2>" +
               $"\n<p><a href=\"{fname}\" class=\"transcript-download-link\" download>" +
               "📄 Download full transcript</a></p>" +
               "\n</section>";
    }

    // Embedded transcript — renders an inline collapsible section (used for local / self-contained pages).
    private static string BuildTranscriptSectionEmbedded(string? transcriptPath, string videoId)
    {
        if (transcriptPath is null || !File.Exists(transcriptPath)) return string.Empty;

        var tsPattern = new Regex(@"^\[(\d{2}):(\d{2})\]\s*(.*)", RegexOptions.Compiled);
        var items     = new StringBuilder();

        foreach (string line in File.ReadLines(transcriptPath))
        {
            var m = tsPattern.Match(line.Trim());
            if (!m.Success) continue;

            int    mins   = int.Parse(m.Groups[1].Value);
            int    secs   = int.Parse(m.Groups[2].Value);
            string text   = m.Groups[3].Value.Trim();
            int    total  = mins * 60 + secs;
            string ytUrl  = HtmlEncode($"https://www.youtube.com/watch?v={videoId}&t={total}");

            items.Append($"<span class=\"ts-line\">");
            items.Append($"<a class=\"ts-link\" href=\"{ytUrl}\" target=\"podslacker-yt\">[{mins:D2}:{secs:D2}]</a>");
            items.Append($" {HtmlEncode(text)}</span>\n");
        }

        if (items.Length == 0) return string.Empty;

        return "\n<section class=\"section\">" +
               "\n<details class=\"transcript-details\">" +
               "\n<summary class=\"transcript-summary\">To see the full transcript, click here</summary>" +
               "\n<div class=\"transcript-body\">\n" +
               items +
               "\n</div>\n</details>\n</section>";
    }

    private static string BuildPlayerBlock(bool hasAudio, string audioElement, string audioFname)
    {
        if (!hasAudio) return string.Empty;

        return "\n<div class=\"player\">" +
               "\n  <div class=\"player-inner\">" +
               "\n    <button class=\"play-btn\" id=\"playBtn\" aria-label=\"Play / Pause\" title=\"Play / Pause (Space)\">" +
               "\n      <svg id=\"iconPlay\" viewBox=\"0 0 24 24\" fill=\"currentColor\" width=\"24\" height=\"24\"><path d=\"M8 5v14l11-7z\"/></svg>" +
               "\n      <svg id=\"iconPause\" viewBox=\"0 0 24 24\" fill=\"currentColor\" width=\"24\" height=\"24\" hidden>" +
               "<path d=\"M6 19h4V5H6v14zm8-14v14h4V5h-4z\"/></svg>" +
               "\n    </button>" +
               $"\n    <div class=\"track-meta\"><span class=\"track-name\">{audioFname}</span></div>" +
               "\n    <div class=\"seek-wrap\">" +
               "\n      <span class=\"time-lbl\" id=\"currentTime\">0:00</span>" +
               "\n      <input type=\"range\" id=\"seekBar\" class=\"seek-bar\" min=\"0\" max=\"100\" step=\"0.1\" value=\"0\">" +
               "\n      <span class=\"time-lbl\" id=\"totalTime\">0:00</span>" +
               "\n    </div>" +
               "\n  </div>" +
               $"\n  {audioElement}" +
               "\n</div>";
    }

    // ── CSS & JS (verbatim port of the Python _PAGE_CSS / _PAGE_JS strings) ──

    private static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);

    private const string PageCss = """
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        :root {
          --bg:      #0d1117;
          --surface: #161b22;
          --border:  #30363d;
          --accent:  #58a6ff;
          --text:    #e6edf3;
          --muted:   #8b949e;
          --code-bg: #1e2433;
          --radius:  10px;
          --ph:      72px;
        }
        html { scroll-behavior: smooth; }
        .ps-cta-bar {
          display: flex; align-items: center; gap: 12px; flex-wrap: wrap;
          background: linear-gradient(90deg, #0d1f3c 0%, #111827 100%);
          border-bottom: 1px solid rgba(88,166,255,.25);
          padding: 10px 24px;
          font-size: 0.84rem;
          color: var(--muted);
        }
        .ps-cta-logo {
          width: 32px; height: 32px; object-fit: contain; border-radius: 50%;
          flex-shrink: 0;
        }
        .ps-cta-bar span { flex: 1; }
        .ps-cta-bar strong { color: var(--text); }
        .ps-cta-btn {
          display: inline-block; padding: 6px 16px; white-space: nowrap;
          background: rgba(88,166,255,.12);
          color: var(--accent);
          border: 1px solid rgba(88,166,255,.35);
          border-radius: 20px;
          font-size: 0.82rem; font-weight: 600;
          text-decoration: none;
          transition: background .2s;
        }
        .ps-cta-btn:hover { background: rgba(88,166,255,.24); }
        body {
          background: var(--bg);
          color: var(--text);
          font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
          font-size: 16px;
          line-height: 1.75;
        }
        body.has-player { padding-bottom: calc(var(--ph) + 8px); }
        .page-header {
          background: linear-gradient(135deg, #1a1f35 0%, #0d1117 100%);
          border-bottom: 1px solid var(--border);
          padding: 36px 24px;
          text-align: center;
        }
        .page-header h1 { font-size: clamp(1.4rem, 4vw, 2.2rem); font-weight: 700; letter-spacing: -0.02em; }
        .vid-badge { display: inline-block; margin-top: 6px; font-size: 0.75rem; color: var(--muted); font-family: "SF Mono", Consolas, monospace; }
        .vid-badge a { color: inherit; text-decoration: none; border-bottom: 1px dotted currentColor; }
        .vid-badge a:hover { opacity: .8; }
        .header-links { display: flex; align-items: center; justify-content: center; flex-wrap: wrap; gap: 10px; margin-top: 14px; }
        .source-link { display: inline-block; padding: 6px 16px; background: rgba(88,166,255,.1); color: var(--accent); border: 1px solid rgba(88,166,255,.3); border-radius: 20px; font-size: 0.84rem; text-decoration: none; transition: background .2s; }
        .source-link:hover { background: rgba(88,166,255,.2); }
        .content { max-width: 860px; margin: 0 auto; padding: 32px 24px; }
        .section { margin-bottom: 48px; }
        .section-heading { font-size: 0.75rem; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: .1em; margin-bottom: 16px; padding-bottom: 8px; border-bottom: 1px solid var(--border); }
        .prose h1, .prose h2, .prose h3, .prose h4 { font-weight: 600; line-height: 1.3; margin: 1.5em 0 .5em; color: var(--text); }
        .prose h1 { font-size: 1.7rem; }
        .prose h2 { font-size: 1.3rem; color: var(--accent); }
        .prose h3 { font-size: 1.1rem; }
        .prose p { margin-bottom: 1em; }
        .prose ul, .prose ol { padding-left: 1.6em; margin-bottom: 1em; }
        .prose li { margin-bottom: .3em; }
        .prose strong { font-weight: 600; }
        .prose code { font-family: "SF Mono", Consolas, monospace; font-size: .875em; background: var(--code-bg); padding: .15em .4em; border-radius: 4px; color: #79c0ff; }
        .prose pre { background: var(--code-bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 16px; overflow-x: auto; margin-bottom: 1em; }
        .prose pre code { background: none; padding: 0; }
        .prose blockquote { border-left: 3px solid var(--accent); padding: 8px 16px; margin: 0 0 1em; color: var(--muted); background: rgba(88,166,255,.06); border-radius: 0 4px 4px 0; }
        .prose table { width: 100%; border-collapse: collapse; margin-bottom: 1em; font-size: .9em; }
        .prose th, .prose td { padding: 8px 12px; border: 1px solid var(--border); text-align: left; }
        .prose th { background: var(--surface); font-weight: 600; }
        .prose tr:nth-child(even) { background: rgba(255,255,255,.02); }
        .prose a { color: var(--accent); text-decoration: none; }
        .prose a:hover { text-decoration: underline; }
        .prose hr { border: none; border-top: 1px solid var(--border); margin: 24px 0; }
        .md-fallback { white-space: pre-wrap; font-size: .88em; color: var(--muted); }
        .gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
        .gal-item { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); overflow: hidden; transition: border-color .2s, transform .2s; }
        .gal-item:hover { border-color: var(--accent); transform: translateY(-2px); }
        .gal-item img { width: 100%; display: block; aspect-ratio: 16/9; object-fit: cover; cursor: zoom-in; }
        .gal-caption { padding: 8px 10px; font-size: .8rem; color: var(--muted); line-height: 1.45; }
        .transcript-details { margin-top: 8px; }
        .transcript-summary { cursor: pointer; color: var(--accent); font-size: 0.9rem; padding: 8px 0; list-style: none; user-select: none; }
        .transcript-summary::-webkit-details-marker { display: none; }
        .transcript-summary::marker { display: none; }
        .transcript-summary::before { content: "▶ "; font-size: .7em; }
        details[open] .transcript-summary::before { content: "▼ "; }
        .transcript-body { margin-top: 12px; display: flex; flex-direction: column; gap: 3px; max-height: 420px; overflow-y: auto; padding: 14px 16px; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); }
        .ts-line { font-size: .875rem; line-height: 1.55; color: var(--text); }
        .ts-link { color: var(--accent); text-decoration: none; font-family: "SF Mono", Consolas, monospace; font-size: .8rem; }
        .ts-link:hover { text-decoration: underline; }
        .transcript-download-link { display: inline-flex; align-items: center; gap: 6px; padding: 8px 18px; background: rgba(88,166,255,.08); color: var(--accent); border: 1px solid rgba(88,166,255,.25); border-radius: 20px; font-size: 0.88rem; text-decoration: none; transition: background .2s; }
        .transcript-download-link:hover { background: rgba(88,166,255,.2); }
        .lightbox { display: none; position: fixed; inset: 0; background: rgba(0,0,0,.88); z-index: 2000; align-items: center; justify-content: center; cursor: zoom-out; }
        .lightbox.open { display: flex; }
        .lightbox-content { display: flex; flex-direction: column; align-items: center; gap: 12px; cursor: default; max-width: 92vw; }
        .lightbox-content img { max-width: 100%; max-height: 80vh; object-fit: contain; border-radius: var(--radius); box-shadow: 0 8px 48px rgba(0,0,0,.7); }
        .lightbox-caption { color: #cdd5e0; font-size: .88rem; line-height: 1.5; text-align: center; max-width: 700px; min-height: 1.2em; }
        .lightbox-close { position: absolute; top: 16px; right: 20px; font-size: 2rem; color: #fff; cursor: pointer; line-height: 1; opacity: .7; transition: opacity .15s; }
        .lightbox-close:hover { opacity: 1; }
        .player { position: fixed; bottom: 0; left: 0; right: 0; height: var(--ph); background: rgba(13,17,23,.93); backdrop-filter: blur(16px); -webkit-backdrop-filter: blur(16px); border-top: 1px solid var(--border); z-index: 1000; }
        .player-inner { max-width: 860px; margin: 0 auto; height: 100%; display: flex; align-items: center; gap: 14px; padding: 0 24px; }
        .play-btn { flex-shrink: 0; width: 42px; height: 42px; border-radius: 50%; background: var(--accent); border: none; cursor: pointer; display: flex; align-items: center; justify-content: center; color: #0d1117; transition: background .15s, transform .1s; }
        .play-btn:hover { background: #79bbff; }
        .play-btn:active { transform: scale(.94); }
        .track-meta { flex-shrink: 0; max-width: 200px; overflow: hidden; }
        .track-name { font-size: .78rem; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-family: "SF Mono", Consolas, monospace; }
        .seek-wrap { flex: 1; display: flex; align-items: center; gap: 10px; }
        .time-lbl { font-size: .76rem; color: var(--muted); font-variant-numeric: tabular-nums; min-width: 34px; font-family: "SF Mono", Consolas, monospace; }
        .seek-bar { flex: 1; -webkit-appearance: none; appearance: none; height: 4px; border-radius: 2px; background: var(--border); cursor: pointer; outline: none; }
        .seek-bar::-webkit-slider-thumb { -webkit-appearance: none; width: 14px; height: 14px; border-radius: 50%; background: var(--accent); cursor: pointer; transition: transform .1s; }
        .seek-bar:hover::-webkit-slider-thumb { transform: scale(1.3); }
        .seek-bar::-moz-range-thumb { width: 14px; height: 14px; border: none; border-radius: 50%; background: var(--accent); cursor: pointer; }
        @media (max-width: 600px) {
          .track-meta { display: none; }
          .page-header { padding: 20px 16px; }
          .content { padding: 20px 16px; }
        }
        """;

    private const string PageJs = """
        (function () {
          var audio = document.getElementById('audioEl');
          if (!audio) return;
          var playBtn   = document.getElementById('playBtn');
          var seekBar   = document.getElementById('seekBar');
          var curTime   = document.getElementById('currentTime');
          var totTime   = document.getElementById('totalTime');
          var iconPlay  = document.getElementById('iconPlay');
          var iconPause = document.getElementById('iconPause');
          function fmt(s) {
            if (isNaN(s) || !isFinite(s)) return '0:00';
            var m = Math.floor(s / 60), sec = Math.floor(s % 60);
            return m + ':' + (sec < 10 ? '0' : '') + sec;
          }
          playBtn.addEventListener('click', function () { audio.paused ? audio.play() : audio.pause(); });
          audio.addEventListener('play',  function () { iconPlay.setAttribute('hidden', ''); iconPause.removeAttribute('hidden'); });
          audio.addEventListener('pause', function () { iconPlay.removeAttribute('hidden'); iconPause.setAttribute('hidden', ''); });
          audio.addEventListener('ended', function () { iconPlay.removeAttribute('hidden'); iconPause.setAttribute('hidden', ''); seekBar.value = 0; curTime.textContent = '0:00'; });
          audio.addEventListener('loadedmetadata', function () { totTime.textContent = fmt(audio.duration); });
          audio.addEventListener('timeupdate', function () { if (!audio.duration) return; seekBar.value = (audio.currentTime / audio.duration) * 100; curTime.textContent = fmt(audio.currentTime); });
          seekBar.addEventListener('input', function () { if (audio.duration) audio.currentTime = (seekBar.value / 100) * audio.duration; });
          document.addEventListener('keydown', function (e) { if (e.code === 'Space' && e.target.tagName !== 'INPUT' && e.target.tagName !== 'TEXTAREA') { e.preventDefault(); audio.paused ? audio.play() : audio.pause(); } });
        }());

        (function () {
          var lb    = document.getElementById('lightbox');
          var lbImg = document.getElementById('lightboxImg');
          var lbCap = document.getElementById('lightboxCaption');
          if (!lb) return;
          function open(src, caption) { lbImg.src = src; if (lbCap) lbCap.textContent = caption || ''; lb.classList.add('open'); }
          function close() { lb.classList.remove('open'); lbImg.src = ''; if (lbCap) lbCap.textContent = ''; }
          document.querySelectorAll('.gal-item img').forEach(function (img) { img.addEventListener('click', function () { open(img.src, img.getAttribute('data-caption')); }); });
          lb.addEventListener('click', function (e) { if (e.target === lb || e.target.classList.contains('lightbox-close')) close(); });
          document.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
        }());

        (function () {
          var ytWin = null;
          document.querySelectorAll('.ts-link').forEach(function (a) {
            a.removeAttribute('target');
            a.addEventListener('click', function (e) {
              e.preventDefault();
              var url = a.href;
              var navigated = false;
              if (ytWin) {
                try {
                  if (!ytWin.closed) { ytWin.location.href = url; navigated = true; try { ytWin.focus(); } catch (_) {} }
                } catch (_) { ytWin = null; }
              }
              if (!navigated) { ytWin = window.open(url, '_blank'); }
            });
          });
        }());
        """;
}
