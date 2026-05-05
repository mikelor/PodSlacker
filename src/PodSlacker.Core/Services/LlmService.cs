using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PodSlacker.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PodSlacker.Core.Services;

/// <summary>
/// Wraps Microsoft.Extensions.AI's IChatClient to provide the three LLM-backed
/// pipeline steps: summary generation, script generation, and key-moment
/// identification.
///
/// Using IChatClient means OpenAI, OpenRouter (via OpenAI-compatible endpoint),
/// Azure OpenAI, Ollama, and any other provider registered in DI all work without
/// any code changes here — only the DI registration differs.
/// </summary>
public sealed class LlmService(ILogger<LlmService> logger)
{
    private const int MaxTranscriptChars = 14_000;

    // ── Prompts ─────────────────────────────────────────────────────────────

    private const string DefaultSummaryPrompt =
        "You are an expert content summarizer. " +
        "Create a clear, well-structured markdown document summarizing a YouTube video transcript. " +
        "Include: a brief overview, major sections/topics covered, key takeaways, and any notable quotes.";

    private const string DefaultDialoguePrompt =
        """
        You are a podcast script writer. Write an engaging two-host podcast episode based on a YouTube transcript.

        Hosts:
        - {host1_name}: analytical and detail-oriented — digs into the "how" and "why".
        - {host2_name}: curious and big-picture — focuses on real-world implications and audience questions.

        Output format — one speaker turn per line, EXACTLY like this (no blank lines between turns):
        [{host1_name}]: <what {host1_name} says>
        [{host2_name}]: <what {host2_name} says>

        Guidelines:
        - Open with both hosts greeting the audience and introducing the topic.
        - Naturally discuss the main ideas; don't just list facts — react, ask questions, build on each other.
        - Each turn should be 2-5 natural sentences (conversational, not lecture-y).
        - Aim for 10-14 total exchanges (20-28 lines).
        - Close with both hosts summarising the takeaways and signing off.
        """;

    private const string DefaultMonologuePrompt =
        """
        You are a podcast script writer. Write an engaging solo-host podcast episode based on a YouTube transcript.

        The host is {host1_name}: knowledgeable and conversational — explains ideas clearly, shares opinions, and keeps the listener hooked.

        Output format — one paragraph per line, EXACTLY like this (no blank lines between paragraphs):
        [{host1_name}]: <what {host1_name} says>

        Guidelines:
        - Open by greeting the audience and introducing the topic.
        - Walk through the main ideas in a natural, flowing narrative — not a bullet-point recap.
        - Each paragraph should be 2-5 sentences (conversational, not lecture-y).
        - Aim for 10-16 paragraphs total.
        - Close with a summary of key takeaways and a sign-off.
        """;

    private const string DefaultKeyMomentsPrompt =
        "You are a video analyst. Given a timestamped transcript excerpt, identify the most " +
        "visually interesting or important moments in the video. " +
        "These should be spread across the video — avoid clustering all picks near the start.\n\n" +
        "Return ONLY a JSON array of exactly {num_frames} numbers representing timestamps " +
        "in seconds (floats). Example: [12.5, 45.0, 120.3, 240.0, 310.5]\n" +
        "No explanation, no markdown, just the JSON array.";

    // ── Public methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Asks the LLM to produce a markdown document summarising a YouTube video.
    /// </summary>
    /// <param name="client">The <see cref="IChatClient"/> to send the request to.</param>
    /// <param name="transcript">Full plain-text transcript of the video.</param>
    /// <param name="url">Source YouTube URL, included in the user message for context.</param>
    /// <param name="model">Model ID passed in <see cref="ChatOptions.ModelId"/>.</param>
    /// <param name="systemPromptOverride">Overrides the built-in system prompt when non-null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The LLM-generated markdown summary.</returns>
    public async Task<string> GenerateSummaryAsync(
        IChatClient   client,
        string        transcript,
        string        url,
        string        model,
        string?       systemPromptOverride = null,
        CancellationToken ct = default)
    {
        string system  = systemPromptOverride ?? DefaultSummaryPrompt;
        string excerpt = Truncate(transcript);
        logger.LogInformation("Generating markdown summary (model: {Model})", model);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User,   $"Source URL: {url}\n\nTranscript:\n{excerpt}"),
            ],
            new ChatOptions { ModelId = model },
            ct);

        return response.Messages[^1].Text?.Trim()
            ?? throw new InvalidOperationException("LLM returned an empty summary.");
    }

    /// <summary>
    /// Asks the LLM to generate a podcast script from a transcript, then parses
    /// the output into a list of labelled <see cref="DialogueSegment"/> turns.
    /// </summary>
    /// <param name="client">The <see cref="IChatClient"/> to send the request to.</param>
    /// <param name="transcript">Full plain-text transcript of the video.</param>
    /// <param name="url">Source YouTube URL, included in the user message for context.</param>
    /// <param name="model">Model ID passed in <see cref="ChatOptions.ModelId"/>.</param>
    /// <param name="hosts">Number of hosts: <c>1</c> generates a monologue, <c>2</c> a dialogue.</param>
    /// <param name="host1Name">Speaker label for the first (or only) host.</param>
    /// <param name="host2Name">Speaker label for the second host; ignored when <paramref name="hosts"/> is <c>1</c>.</param>
    /// <param name="dialoguePromptOverride">Replaces the built-in two-host system prompt when non-null.</param>
    /// <param name="monologuePromptOverride">Replaces the built-in solo-host system prompt when non-null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of speaker turns ready for TTS synthesis.</returns>
    public async Task<List<DialogueSegment>> GenerateScriptAsync(
        IChatClient   client,
        string        transcript,
        string        url,
        string        model,
        int           hosts      = 2,
        string        host1Name  = "ALEX",
        string        host2Name  = "JORDAN",
        string?       dialoguePromptOverride  = null,
        string?       monologuePromptOverride = null,
        CancellationToken ct = default)
    {
        string rawPrompt = hosts == 1
            ? (monologuePromptOverride ?? DefaultMonologuePrompt)
            : (dialoguePromptOverride  ?? DefaultDialoguePrompt);

        string system = rawPrompt
            .Replace("{host1_name}", host1Name)
            .Replace("{host2_name}", host2Name);

        string excerpt   = Truncate(transcript);
        string hostLabel = hosts == 1 ? "solo monologue" : "two-host dialogue";
        logger.LogInformation("Generating {Label} script (model: {Model})", hostLabel, model);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User,   $"Source URL: {url}\n\nTranscript:\n{excerpt}"),
            ],
            new ChatOptions { ModelId = model },
            ct);

        string rawScript = response.Messages[^1].Text?.Trim()
            ?? throw new InvalidOperationException("LLM returned an empty script.");

        return ParseScript(rawScript, host1Name, host2Name, hosts);
    }

    /// <summary>
    /// Asks the LLM to pick the most visually interesting timestamps from a timed
    /// transcript, returning them as a sorted list of seconds suitable for frame capture.
    /// </summary>
    /// <param name="client">The <see cref="IChatClient"/> to send the request to.</param>
    /// <param name="timedEntries">Timed transcript entries used to build the prompt.</param>
    /// <param name="numFrames">Exact number of timestamps to request from the LLM.</param>
    /// <param name="model">Model ID passed in <see cref="ChatOptions.ModelId"/>.</param>
    /// <param name="url">Source YouTube URL, included in the user message for context.</param>
    /// <param name="systemPromptOverride">Replaces the built-in key-moments system prompt when non-null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Sorted list of timestamps in seconds, clamped to the video duration and
    /// limited to <paramref name="numFrames"/> entries.
    /// </returns>
    public async Task<List<double>> IdentifyKeyMomentsAsync(
        IChatClient                  client,
        List<TimedTranscriptEntry>   timedEntries,
        int                          numFrames,
        string                       model,
        string                       url,
        string?                      systemPromptOverride = null,
        CancellationToken            ct = default)
    {
        string rawPrompt = systemPromptOverride ?? DefaultKeyMomentsPrompt;
        string system    = rawPrompt.Replace("{num_frames}", numFrames.ToString());

        // Build a compact timestamped transcript (~1 entry per 10 s).
        var sampled    = new List<string>();
        double lastTs  = -999;
        foreach (var entry in timedEntries)
        {
            if (entry.StartSeconds - lastTs >= 10.0)
            {
                int m = (int)(entry.StartSeconds / 60);
                int s = (int)(entry.StartSeconds % 60);
                sampled.Add($"[{m:D2}:{s:D2}] {entry.Text.Trim()}");
                lastTs = entry.StartSeconds;
            }
        }

        double totalDuration = timedEntries.Count > 0 ? timedEntries[^1].StartSeconds : 0;
        string userMsg =
            $"Video URL: {url}\n" +
            $"Total duration (approx): {(int)(totalDuration / 60)}m {(int)(totalDuration % 60)}s\n\n" +
            $"Timestamped transcript:\n{string.Join('\n', sampled)}";

        logger.LogInformation("Asking LLM to identify {N} key moments (model: {Model})", numFrames, model);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User,   userMsg),
            ],
            new ChatOptions { ModelId = model, Temperature = 0.3f },
            ct);

        string raw = response.Messages[^1].Text?.Trim()
            ?? throw new InvalidOperationException("LLM returned empty key-moments response.");

        // Strip markdown code fences if the model added them.
        raw = Regex.Replace(raw, @"```[a-z]*\n?", "").Trim();

        var timestamps = JsonSerializer.Deserialize<List<double>>(raw)
            ?? throw new InvalidOperationException("Could not parse key-moments JSON array.");

        double maxTs = totalDuration > 0 ? totalDuration : double.MaxValue;
        return [.. timestamps
            .Select(t => Math.Clamp(t, 0, maxTs))
            .OrderBy(t => t)
            .Take(numFrames)];
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Truncate(string text) =>
        text.Length <= MaxTranscriptChars
            ? text
            : text[..MaxTranscriptChars] + "\n\n[transcript truncated for length]";

    private static List<DialogueSegment> ParseScript(
        string rawScript, string host1, string host2, int hosts)
    {
        var tags     = hosts == 2 ? [host1, host2] : (string[])[host1];
        var segments = new List<DialogueSegment>();

        foreach (string line in rawScript.Split('\n'))
        {
            string trimmed = line.Trim();
            foreach (string tag in tags)
            {
                string prefix = $"[{tag}]:";
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    segments.Add(new DialogueSegment(tag, trimmed[prefix.Length..].Trim()));
                    break;
                }
            }
        }

        if (segments.Count == 0)
            throw new InvalidOperationException(
                "Could not parse any script lines from the LLM response.\nRaw output:\n" + rawScript);

        return segments;
    }

    // ── Prompt loading ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads a prompt using a three-tier priority chain:
    ///
    ///   1. Explicit override path supplied by the caller (CLI --summary-prompt flag).
    ///   2. A <c>prompts/{name}.txt</c> file in the same directory as the running
    ///      executable — lets users customise prompts without recompiling.
    ///   3. The prompt embedded as a resource in <c>PodSlacker.Core.dll</c> —
    ///      the canonical version that is identical whether the code runs as a CLI
    ///      tool, an ASP.NET API, or an Azure Function.
    ///
    /// The embedded resources live in <c>PodSlacker.Core/prompts/</c> and are
    /// declared in the .csproj with explicit <c>LogicalName</c> attributes so their
    /// resource keys are stable regardless of build configuration.
    /// </summary>
    public static string LoadPrompt(string name, string? overridePath, string executableDir)
    {
        // ── Tier 1: explicit file path (from config or CLI flag) ─────────────
        // Relative paths are resolved first against the working directory, then
        // against the executable directory — so "prompts/summary.txt" in
        // podslacker.json finds the file copied next to the exe regardless of
        // where the user invokes the command from.
        if (overridePath is not null)
        {
            string resolved = File.Exists(overridePath)
                ? overridePath
                : Path.Combine(AppContext.BaseDirectory, overridePath);

            if (!File.Exists(resolved))
            {
                // Neither location has the file — silently fall through so that
                // a path in podslacker.json acts as a self-documenting hint
                // without breaking runs where the file hasn't been created yet.
            }
            else
            {
                string text = File.ReadAllText(resolved, System.Text.Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    Console.WriteLine($"   Loaded {name} prompt from: {resolved}");
                    return text;
                }
                Console.WriteLine($"   Warning: prompt file '{resolved}' is empty — falling back.");
            }
        }

        // ── Tier 2: prompts/ folder next to the executable ────────────────────
        string localPath = Path.Combine(executableDir, "prompts", $"{name}.txt");
        if (File.Exists(localPath))
        {
            string text = File.ReadAllText(localPath, System.Text.Encoding.UTF8).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                Console.WriteLine($"   Loaded {name} prompt from: {localPath}");
                return text;
            }
            Console.WriteLine($"   Warning: {localPath} is empty — falling back to embedded prompt.");
        }

        // ── Tier 3: embedded resource in PodSlacker.Core.dll ─────────────────
        string resourceKey = $"PodSlacker.Core.prompts.{name}.txt";
        var assembly = typeof(LlmService).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceKey);

        if (stream is not null)
        {
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            string text = reader.ReadToEnd().Trim();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        // Should never reach here as long as the .csproj EmbeddedResource entries exist.
        throw new InvalidOperationException(
            $"Could not load prompt '{name}'. " +
            $"Expected embedded resource '{resourceKey}' in PodSlacker.Core.dll. " +
            $"Verify the EmbeddedResource entries in PodSlacker.Core.csproj.");
    }
}
