using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using PodSlacker.Core.Models;
using PodSlacker.Core.Services;
using System.Text.Json;

namespace PodSlacker.Core.Pipeline;

/// <summary>
/// Orchestrates the full PodSlacker pipeline:
///   1. Fetch title + transcript
///   2. Generate summary (LLM)
///   3. Generate podcast script (LLM)
///   4. Generate audio (TTS)
///   5. Capture key-moment frames (OpenCV + yt-dlp)
///   6. Generate HTML page
///   7. Publish to GitHub Pages (optional)
///
/// Progress is reported via IProgress&lt;PipelineProgress&gt; so both the CLI
/// and the future web API can surface live status.
///
/// The IChatClient is wired up by the caller (CLI or API) using
/// Microsoft.Extensions.AI — OpenAI, OpenRouter, Ollama, and Azure OpenAI
/// all work without any changes here.
/// </summary>
public sealed class PodSlackerPipeline(
    TranscriptService    transcriptService,
    VideoMetadataService metadataService,
    LlmService           llmService,
    TtsService           ttsService,
    KokoroTtsService     kokoroTtsService,
    FrameCaptureService  frameCaptureService,
    GitHubPublishService githubService,
    ILogger<PodSlackerPipeline> logger)
{
    // ── Entry point ──────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the full PodSlacker pipeline end-to-end for the given YouTube URL,
    /// reporting progress at each step via <paramref name="progress"/>.
    /// </summary>
    /// <param name="videoUrl">The YouTube video URL to process.</param>
    /// <param name="config">Runtime configuration (output paths, LLM settings, TTS voices, etc.).</param>
    /// <param name="baseChatClient">
    /// The primary <see cref="IChatClient"/> used for all LLM steps unless a
    /// per-step override is configured in <paramref name="config"/>.
    /// </param>
    /// <param name="ttsClient">
    /// OpenAI <see cref="AudioClient"/> used for TTS synthesis.
    /// Pass <see langword="null"/> (or set <see cref="PodSlackerConfig.NoAudio"/>) to skip audio.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink; receives a <see cref="PipelineProgress"/> notification
    /// after each pipeline step, suitable for a CLI progress line or SSE stream.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PipelineResult"/> describing the paths of all generated artefacts
    /// and, if publishing was requested, the live GitHub Pages URL.
    /// </returns>
    public async Task<PipelineResult> RunAsync(
        string                  videoUrl,
        PodSlackerConfig        config,
        IChatClient             baseChatClient,
        AudioClient?            ttsClient          = null,
        IProgress<PipelineProgress>? progress      = null,
        CancellationToken       ct                 = default)
    {
        void Report(PipelineStep step, string msg, int pct = 0) =>
            progress?.Report(new PipelineProgress(step, msg, pct));

        // ── 0. Resolve video ID and output paths ────────────────────────────
        string videoId  = VideoMetadataService.ExtractVideoId(videoUrl);
        string outputDir = Path.GetFullPath(config.OutputDir);
        Directory.CreateDirectory(outputDir);

        // ── 1. Fetch title ──────────────────────────────────────────────────
        Report(PipelineStep.FetchingTitle, "Fetching video title…", 2);
        string? title    = await metadataService.FetchTitleAsync(videoUrl, ct);
        string  baseName = title is not null
            ? $"{VideoMetadataService.SanitizeTitle(title)}_{videoId}"
            : videoId;

        // Report the resolved title so callers (e.g. PipelineRunner) can surface it early.
        progress?.Report(new PipelineProgress(
            PipelineStep.FetchingTitle,
            title is not null ? $"Title: {title}" : "Title not found",
            5,
            title ?? videoId));

        logger.LogInformation("Title: {Title}", title ?? "(not found)");
        logger.LogInformation("Base name: {BaseName}", baseName);

        // ── 2. Fetch transcript ─────────────────────────────────────────────
        Report(PipelineStep.FetchingTranscript, "Fetching transcript…", 8);
        var (transcript, timedEntries) = await transcriptService.FetchAsync(
            videoId, config.Cookies, config.Proxy, ct);

        // Write transcript file (timestamped format for the HTML page).
        string transcriptPath = Path.Combine(outputDir, $"{baseName}_transcript.txt");
        await WriteTimedTranscriptAsync(timedEntries, transcriptPath, ct);

        // ── 3. Summary & script ──────────────────────────────────────────────
        string mdPath = Path.Combine(outputDir, $"{baseName}_summary.md");
        string summary;
        List<DialogueSegment> segments;

        if (config.ReuseSummary && File.Exists(mdPath))
        {
            logger.LogInformation("Reusing existing summary from {Path}", mdPath);
            (summary, segments) = MarkdownService.ParseMarkdown(mdPath);
        }
        else
        {
            // Step-specific LLM clients (may reuse base if identical settings).
            var summaryClient    = BuildStepClient(config.SummaryBaseUrl,    config.SummaryApiKeyEnv,    config, baseChatClient);
            var scriptClient     = BuildStepClient(config.ScriptBaseUrl,     config.ScriptApiKeyEnv,     config, baseChatClient);

            Report(PipelineStep.GeneratingSummary, "Generating summary…", 15);
            string summaryModel  = config.SummaryModel ?? config.LlmModel;
            string summaryPrompt = LlmService.LoadPrompt("summary",     config.SummaryPrompt,   ExeDir);
            summary = await llmService.GenerateSummaryAsync(
                summaryClient, transcript, videoUrl, summaryModel, summaryPrompt, ct);

            Report(PipelineStep.GeneratingScript, "Generating podcast script…", 35);
            string scriptModel   = config.ScriptModel ?? config.LlmModel;
            string dialoguePmt   = LlmService.LoadPrompt("dialogue",   config.DialoguePrompt,  ExeDir);
            string monologuePmt  = LlmService.LoadPrompt("monologue",  config.MonologuePrompt, ExeDir);
            segments = await llmService.GenerateScriptAsync(
                scriptClient, transcript, videoUrl, scriptModel,
                config.Hosts, config.Host1Name, config.Host2Name,
                dialoguePmt, monologuePmt, ct);

            // Persist markdown.
            string md = MarkdownService.BuildMarkdown(videoUrl, videoId, summary, segments, title);
            await File.WriteAllTextAsync(mdPath, md, System.Text.Encoding.UTF8, ct);
            logger.LogInformation("Summary written to {Path}", mdPath);
        }

        // ── 4. Audio ─────────────────────────────────────────────────────────
        string? audioPath = null;
        if (!config.NoAudio)
        {
            bool isKokoro = config.TtsEngine.Equals("kokoro", StringComparison.OrdinalIgnoreCase);
            string ext    = isKokoro ? ".wav" : ".mp3";
            audioPath     = Path.Combine(outputDir, $"{baseName}_podcast{ext}");

            Report(PipelineStep.GeneratingAudio, $"Generating audio… (0 / {segments.Count} segments)", 50);

            // Per-segment callback: maps segment index into the 50–65 % range and
            // updates the progress message so the UI shows live segment counts.
            // Audio occupies the 50–65 % band; frame capture starts at 65 %.
            const int audioStartPct = 50;
            const int audioEndPct   = 65;
            var segmentProgress = new Progress<(int current, int total)>(t =>
            {
                int pct = t.total > 0
                    ? audioStartPct + (t.current * (audioEndPct - audioStartPct) / t.total)
                    : audioStartPct;
                progress?.Report(new PipelineProgress(
                    PipelineStep.GeneratingAudio,
                    $"Generating audio… ({t.current} / {t.total} segments)",
                    pct));
            });

            if (isKokoro)
            {
                await kokoroTtsService.GenerateAudioAsync(
                    segments, audioPath,
                    config.VoiceHost1, config.VoiceHost2,
                    config.Host1Name, config.Host2Name,
                    segmentProgress, ct);
            }
            else if (ttsClient is not null)
            {
                await ttsService.GenerateAudioAsync(
                    ttsClient, segments, audioPath,
                    config.VoiceHost1, config.VoiceHost2,
                    config.TtsModel, config.Host1Name, config.Host2Name,
                    segmentProgress, ct);
            }
            else
            {
                logger.LogWarning(
                    "TtsEngine is '{Engine}' but no AudioClient was provided — " +
                    "skipping audio generation. Set an OpenAI API key or switch to kokoro.",
                    config.TtsEngine);
                audioPath = null;
            }
        }

        // ── 5. Frame capture ─────────────────────────────────────────────────
        var capturedFrames = new List<CapturedFrame>();
        List<string> frameCaptions = [];

        if (!config.NoPage && config.NumFrames > 0)
        {
            try
            {
                Report(PipelineStep.CapturingFrames, "Identifying key moments…", 65);
                var kmClient = BuildStepClient(
                    config.KeyMomentsBaseUrl, config.KeyMomentsApiKeyEnv, config, baseChatClient);
                string kmModel  = config.KeyMomentsModel ?? config.LlmModel;
                string kmPrompt = LlmService.LoadPrompt("key_moments", config.KeyMomentsPrompt, ExeDir);

                var timestamps = await llmService.IdentifyKeyMomentsAsync(
                    kmClient, timedEntries, config.NumFrames, kmModel, videoUrl, kmPrompt, ct);

                string streamUrl = await metadataService.GetStreamUrlAsync(videoUrl, ct);

                // Per-frame callback: maps frame index into the 72–85 % band so the
                // progress bar advances smoothly as each JPEG is saved.
                const int framesStartPct = 72;
                const int framesEndPct   = 85;
                int       frameTotal     = timestamps.Count;
                Report(PipelineStep.CapturingFrames,
                    $"Capturing frames… (0 / {frameTotal})", framesStartPct);

                var frameProgress = new Progress<(int current, int total)>(t =>
                {
                    int pct = t.total > 0
                        ? framesStartPct + (t.current * (framesEndPct - framesStartPct) / t.total)
                        : framesStartPct;
                    progress?.Report(new PipelineProgress(
                        PipelineStep.CapturingFrames,
                        $"Capturing frames… ({t.current} / {t.total})",
                        pct));
                });

                capturedFrames = frameCaptureService.CaptureFrames(
                    streamUrl, timestamps, outputDir, baseName, frameProgress);
                frameCaptions    = FrameCaptureService.GetFrameCaptions(
                    timedEntries, capturedFrames.Select(f => f.TimestampSeconds).ToList());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Frame capture failed — continuing without frames.");
            }
        }

        // ── 6. HTML page ─────────────────────────────────────────────────────
        string? htmlPath = null;
        IReadOnlyList<string> pageAssets = [];
        if (!config.NoPage)
        {
            // When publishing to GitHub, default to referenced (non-embedded) assets
            // unless the config explicitly requests embedding.  Referenced mode uploads
            // audio and images as separate files so the HTML stays small and GitHub
            // Pages serves it correctly as HTML rather than a binary blob.
            bool embedAssets = config.PublishGithub
                ? config.GithubEmbedAssets
                : true;   // local-only runs keep the self-contained single-file behaviour

            Report(PipelineStep.GeneratingPage, "Generating HTML page…", 85);
            var pageResult = PageGeneratorService.GeneratePage(
                videoId, videoUrl, outputDir,
                audioPath,
                capturedFrames.Select(f => f.FilePath).ToList(),
                mdPath,
                title,
                baseName,
                transcriptPath,
                frameCaptions,
                embedAssets);
            htmlPath   = pageResult.HtmlPath;
            pageAssets = pageResult.AssetPaths;
            logger.LogInformation("HTML page written to {Path}", htmlPath);
        }

        // ── 7. GitHub Pages ───────────────────────────────────────────────────
        string? ghPagesUrl = null;
        if (config.PublishGithub && htmlPath is not null)
        {
            Report(PipelineStep.Publishing, "Publishing to GitHub Pages…", 95);
            ghPagesUrl = await githubService.PublishAsync(
                htmlPath, config.GithubRepo, config.GithubTokenEnv,
                config.GithubBranch, config.GithubTokenValue,
                pageAssets.Count > 0 ? pageAssets : null,
                config.GithubFolder,
                ct);
            logger.LogInformation("Published to: {Url}", ghPagesUrl);
        }

        Report(PipelineStep.Done, "Done!", 100);

        return new PipelineResult
        {
            VideoId       = videoId,
            Title         = title ?? videoId,
            MarkdownPath  = mdPath,
            AudioPath     = audioPath,
            HtmlPagePath  = htmlPath,
            GitHubPagesUrl = ghPagesUrl,
            Frames        = capturedFrames,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a step-specific IChatClient if the step has different settings from
    /// the base LLM, otherwise reuses the base client — mirrors _build_step_client()
    /// in Python.
    /// </summary>
    private static IChatClient BuildStepClient(
        string?         stepBaseUrl,
        string?         stepApiKeyEnv,
        PodSlackerConfig config,
        IChatClient     baseChatClient)
    {
        string? effectiveUrl    = stepBaseUrl    ?? config.LlmBaseUrl;
        string  effectiveKeyEnv = stepApiKeyEnv  ?? config.LlmApiKeyEnv;

        // If identical to base config, reuse.
        if (effectiveUrl == config.LlmBaseUrl && effectiveKeyEnv == config.LlmApiKeyEnv)
            return baseChatClient;

        string apiKey = Environment.GetEnvironmentVariable(effectiveKeyEnv)
            ?? throw new InvalidOperationException(
                $"API key environment variable '{effectiveKeyEnv}' is not set.");

        var openAiClient = effectiveUrl is not null
            ? new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey),
                               new OpenAIClientOptions { Endpoint = new Uri(effectiveUrl) })
            : new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey));

        return openAiClient.GetChatClient(config.LlmModel).AsIChatClient();
    }

    private static async Task WriteTimedTranscriptAsync(
        List<TimedTranscriptEntry> entries,
        string outputPath,
        CancellationToken ct)
    {
        using var writer = new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
        foreach (var entry in entries)
        {
            int m = (int)(entry.StartSeconds / 60);
            int s = (int)(entry.StartSeconds % 60);
            await writer.WriteLineAsync($"[{m:D2}:{s:D2}] {entry.Text}");
        }
    }

    // The executable directory is used only for tier-2 prompt file lookup.
    // Tier-3 (embedded resources) does not need a path at all.
    private static string ExeDir => AppContext.BaseDirectory;

    // ── Config loading (mirrors Python load_config()) ─────────────────────────

    /// <summary>
    /// Loads a podslacker.json config file and deserialises it into a
    /// <see cref="PodSlackerConfig"/> instance.  Null values in the JSON are
    /// silently skipped so CLI defaults take precedence.
    /// </summary>
    public static PodSlackerConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
            return new PodSlackerConfig();

        try
        {
            string json = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
            // Strip comment keys (keys starting with "_") before deserialising.
            using var doc = JsonDocument.Parse(json);
            var filtered  = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.StartsWith('_') && prop.Value.ValueKind != JsonValueKind.Null)
                    filtered[prop.Name] = prop.Value.Clone();
            }
            string cleaned = JsonSerializer.Serialize(filtered);
            return JsonSerializer.Deserialize<PodSlackerConfig>(cleaned,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
                ?? new PodSlackerConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️  Could not parse config file {configPath}: {ex.Message}");
            return new PodSlackerConfig();
        }
    }
}
