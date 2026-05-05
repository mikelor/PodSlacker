using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using PodSlacker.Core.Models;
using PodSlacker.Core.Pipeline;
using PodSlacker.Core.Services;
using System.ClientModel;

// ── Root command ─────────────────────────────────────────────────────────────

var root = new RootCommand("PodSlacker — convert a YouTube video into a podcast episode");

// ── 'generate' subcommand ────────────────────────────────────────────────────

var generateCmd = new Command("generate", "Process a YouTube video and produce a podcast");
root.Add(generateCmd);

// Positional argument
var urlArg = new Argument<string>("url") { Description = "YouTube video URL" };
generateCmd.Add(urlArg);

// ── All options (mirrors Python argparse exactly) ─────────────────────────────

// Output — nullable so a missing flag falls through to the config file value.
var outputDirOpt = new Option<string?>("--output-dir", ["-o"])
    { Description = "Directory for output files (default: value from podslacker.json, otherwise 'outputs')" };

// YouTube access
var cookiesOpt = new Option<string?>("--cookies")  { Description = "Path to a Netscape-format cookies.txt file" };
var proxyOpt   = new Option<string?>("--proxy")    { Description = "Proxy URL, e.g. http://user:pass@host:port" };

// Behaviour flags — bool flags default to false, so no special handling needed.
// Int/string flags are nullable so a missing CLI flag returns null and the
// config file value flows through; PodSlackerConfig holds the final defaults.
var noAudioOpt      = new Option<bool>("--no-audio")       { Description = "Skip audio generation" };
var noPageOpt       = new Option<bool>("--no-page")        { Description = "Skip HTML page generation" };
var reuseSummaryOpt = new Option<bool>("--reuse-summary")  { Description = "Reuse existing summary markdown if present" };
var numFramesOpt    = new Option<int?>("--num-frames")     { Description = "Number of key-moment frames to capture (default: 6)" };
var hostsOpt        = new Option<int?>("--hosts")          { Description = "Number of podcast hosts: 1 or 2 (default: 2)" };

// Host names
var host1NameOpt = new Option<string?>("--host1-name") { Description = "Name for host 1 (default: MIKE)" };
var host2NameOpt = new Option<string?>("--host2-name") { Description = "Name for host 2 (default: JORDAN)" };

// Base LLM
var llmBaseUrlOpt   = new Option<string?>("--llm-base-url")   { Description = "LLM API base URL (default: https://openrouter.ai/api/v1)" };
var llmModelOpt     = new Option<string?>("--llm-model")      { Description = "LLM model name (default: openrouter/auto:free)" };
var llmApiKeyEnvOpt = new Option<string?>("--llm-api-key-env"){ Description = "Env var for LLM API key (default: OPENROUTER_API_KEY)" };

// Per-step LLM overrides
var summaryModelOpt   = new Option<string?>("--summary-model")           { Description = "LLM model for summarisation step" };
var summaryBaseUrlOpt = new Option<string?>("--summary-base-url")        { Description = "LLM base URL for summarisation step" };
var summaryApiKeyOpt  = new Option<string?>("--summary-api-key-env")     { Description = "Env var for summarisation API key" };
var scriptModelOpt    = new Option<string?>("--script-model")            { Description = "LLM model for script generation step" };
var scriptBaseUrlOpt  = new Option<string?>("--script-base-url")         { Description = "LLM base URL for script step" };
var scriptApiKeyOpt   = new Option<string?>("--script-api-key-env")      { Description = "Env var for script API key" };
var kmModelOpt        = new Option<string?>("--key-moments-model")       { Description = "LLM model for key moments step" };
var kmBaseUrlOpt      = new Option<string?>("--key-moments-base-url")    { Description = "LLM base URL for key moments step" };
var kmApiKeyOpt       = new Option<string?>("--key-moments-api-key-env") { Description = "Env var for key moments API key" };

// TTS
var ttsEngineOpt = new Option<string?>("--tts-engine")
    { Description = "TTS engine: 'kokoro' (local, default) or 'openai' (cloud)" };
var ttsModelOpt  = new Option<string?>("--tts-model")      { Description = "TTS model, OpenAI only (default: tts-1)" };
var ttsApiKeyOpt = new Option<string?>("--tts-api-key-env"){ Description = "Env var for TTS API key, OpenAI only (default: OPENAI_API_KEY)" };
var voice1Opt    = new Option<string?>("--voice-host1")    { Description = "Voice for host 1 — Kokoro (e.g. am_michael) or OpenAI (e.g. onyx)" };
var voice2Opt    = new Option<string?>("--voice-host2")    { Description = "Voice for host 2 — Kokoro (e.g. af_heart) or OpenAI (e.g. nova)" };

// Prompt overrides
var summaryPromptOpt   = new Option<string?>("--summary-prompt")     { Description = "Path to a custom summary prompt file" };
var dialoguePromptOpt  = new Option<string?>("--dialogue-prompt")    { Description = "Path to a custom dialogue prompt file" };
var monologuePromptOpt = new Option<string?>("--monologue-prompt")   { Description = "Path to a custom monologue prompt file" };
var kmPromptOpt        = new Option<string?>("--key-moments-prompt") { Description = "Path to a custom key-moments prompt file" };

// GitHub Pages
var publishGhOpt = new Option<bool>("--publish-github")   { Description = "Publish HTML page to GitHub Pages" };
var ghRepoOpt    = new Option<string?>("--github-repo")   { Description = "GitHub repo name (default: podslacker-pages)" };
var ghTokenOpt   = new Option<string?>("--github-token-env") { Description = "Env var for GitHub token (default: GITHUB_TOKEN)" };
var ghBranchOpt  = new Option<string?>("--github-branch") { Description = "GitHub Pages branch (default: gh-pages)" };

// Config file
var configOpt = new Option<string?>("--config") { Description = "Path to podslacker.json config file" };

// Output file for remote mode (where to save the downloaded HTML)
var outputFileOpt = new Option<string?>("--output-file")
    { Description = "Path where the HTML page is saved in remote mode (default: <current dir>/<video-id>.html)" };

// Add all options to the generate command
foreach (var opt in new Option[]
{
    outputDirOpt, cookiesOpt, proxyOpt, noAudioOpt, noPageOpt,
    reuseSummaryOpt, numFramesOpt, hostsOpt, host1NameOpt, host2NameOpt,
    llmBaseUrlOpt, llmModelOpt, llmApiKeyEnvOpt,
    summaryModelOpt, summaryBaseUrlOpt, summaryApiKeyOpt,
    scriptModelOpt, scriptBaseUrlOpt, scriptApiKeyOpt,
    kmModelOpt, kmBaseUrlOpt, kmApiKeyOpt,
    ttsEngineOpt, ttsModelOpt, ttsApiKeyOpt, voice1Opt, voice2Opt,
    summaryPromptOpt, dialoguePromptOpt, monologuePromptOpt, kmPromptOpt,
    publishGhOpt, ghRepoOpt, ghTokenOpt, ghBranchOpt,
    configOpt, outputFileOpt,
})
{
    generateCmd.Add(opt);
}

// ── Handler ───────────────────────────────────────────────────────────────────

generateCmd.SetAction(async (ParseResult result, CancellationToken ct) =>
{
    string  url        = result.GetValue(urlArg)!;
    string? serviceUrl = Environment.GetEnvironmentVariable("PODSLACKER_SERVICE_URL");

    // ── Build config (JSON file base + CLI overrides) ────────────────────────

    string? configPath = result.GetValue(configOpt) ?? FindDefaultConfig();

    PodSlackerConfig fileConfig = configPath is not null
        ? PodSlackerPipeline.LoadConfig(configPath)
        : new PodSlackerConfig();

    // Priority: CLI flag  >  config file  >  PodSlackerConfig compiled default
    PodSlackerConfig config = new()
    {
        OutputDir    = result.GetValue(outputDirOpt)    ?? fileConfig.OutputDir,
        Hosts        = result.GetValue(hostsOpt)        ?? fileConfig.Hosts,
        Host1Name    = result.GetValue(host1NameOpt)    ?? fileConfig.Host1Name,
        Host2Name    = result.GetValue(host2NameOpt)    ?? fileConfig.Host2Name,
        NoAudio      = result.GetValue(noAudioOpt)      || fileConfig.NoAudio,
        NoPage       = result.GetValue(noPageOpt)       || fileConfig.NoPage,
        ReuseSummary = result.GetValue(reuseSummaryOpt) || fileConfig.ReuseSummary,
        NumFrames    = result.GetValue(numFramesOpt)    ?? fileConfig.NumFrames,
        Cookies      = result.GetValue(cookiesOpt)      ?? fileConfig.Cookies,
        Proxy        = result.GetValue(proxyOpt)        ?? fileConfig.Proxy,

        // LLM
        LlmModel     = result.GetValue(llmModelOpt)     ?? fileConfig.LlmModel,
        LlmBaseUrl   = result.GetValue(llmBaseUrlOpt)   ?? fileConfig.LlmBaseUrl,
        LlmApiKeyEnv = result.GetValue(llmApiKeyEnvOpt) ?? fileConfig.LlmApiKeyEnv,

        // Per-step overrides
        SummaryModel        = result.GetValue(summaryModelOpt)   ?? fileConfig.SummaryModel,
        SummaryBaseUrl      = result.GetValue(summaryBaseUrlOpt) ?? fileConfig.SummaryBaseUrl,
        SummaryApiKeyEnv    = result.GetValue(summaryApiKeyOpt)  ?? fileConfig.SummaryApiKeyEnv,
        ScriptModel         = result.GetValue(scriptModelOpt)    ?? fileConfig.ScriptModel,
        ScriptBaseUrl       = result.GetValue(scriptBaseUrlOpt)  ?? fileConfig.ScriptBaseUrl,
        ScriptApiKeyEnv     = result.GetValue(scriptApiKeyOpt)   ?? fileConfig.ScriptApiKeyEnv,
        KeyMomentsModel     = result.GetValue(kmModelOpt)        ?? fileConfig.KeyMomentsModel,
        KeyMomentsBaseUrl   = result.GetValue(kmBaseUrlOpt)      ?? fileConfig.KeyMomentsBaseUrl,
        KeyMomentsApiKeyEnv = result.GetValue(kmApiKeyOpt)       ?? fileConfig.KeyMomentsApiKeyEnv,

        // TTS
        TtsEngine    = result.GetValue(ttsEngineOpt) ?? fileConfig.TtsEngine,
        TtsModel     = result.GetValue(ttsModelOpt)  ?? fileConfig.TtsModel,
        TtsApiKeyEnv = result.GetValue(ttsApiKeyOpt) ?? fileConfig.TtsApiKeyEnv,
        VoiceHost1   = result.GetValue(voice1Opt)    ?? fileConfig.VoiceHost1,
        VoiceHost2   = result.GetValue(voice2Opt)    ?? fileConfig.VoiceHost2,

        // Prompts
        SummaryPrompt    = result.GetValue(summaryPromptOpt)   ?? fileConfig.SummaryPrompt,
        DialoguePrompt   = result.GetValue(dialoguePromptOpt)  ?? fileConfig.DialoguePrompt,
        MonologuePrompt  = result.GetValue(monologuePromptOpt) ?? fileConfig.MonologuePrompt,
        KeyMomentsPrompt = result.GetValue(kmPromptOpt)        ?? fileConfig.KeyMomentsPrompt,

        // GitHub
        PublishGithub  = result.GetValue(publishGhOpt) || fileConfig.PublishGithub,
        GithubRepo     = result.GetValue(ghRepoOpt)     ?? fileConfig.GithubRepo,
        GithubTokenEnv = result.GetValue(ghTokenOpt)    ?? fileConfig.GithubTokenEnv,
        GithubBranch   = result.GetValue(ghBranchOpt)   ?? fileConfig.GithubBranch,
    };

    // If PODSLACKER_SERVICE_URL is set, delegate to the remote API.
    if (!string.IsNullOrEmpty(serviceUrl))
    {
        string? outputFile = result.GetValue(outputFileOpt);
        return await RunRemoteAsync(url, serviceUrl, config, outputFile, ct);
    }

    // ── Build DI host ─────────────────────────────────────────────────────────

    var host = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole(opts => opts.FormatterName = "simple");
            logging.SetMinimumLevel(LogLevel.Information);
        })
        .ConfigureServices(services =>
        {
            services.AddTransient<TranscriptService>();
            services.AddTransient<VideoMetadataService>();
            services.AddTransient<LlmService>();
            services.AddTransient<TtsService>();
            services.AddTransient<KokoroTtsService>();
            services.AddTransient<FrameCaptureService>();
            services.AddTransient<GitHubPublishService>();
            services.AddTransient<PodSlackerPipeline>();
        })
        .Build();

    // ── Build LLM chat client ─────────────────────────────────────────────────

    IChatClient  chatClient  = PipelineClientFactory.CreateChatClient(config);
    AudioClient? audioClient = PipelineClientFactory.CreateAudioClient(config);

    // ── Run pipeline ──────────────────────────────────────────────────────────

    var pipeline = host.Services.GetRequiredService<PodSlackerPipeline>();

    var progress = new Progress<PipelineProgress>(p =>
    {
        string icon = p.Step switch
        {
            PipelineStep.Done  => "✅",
            PipelineStep.Error => "❌",
            _                  => "⏳",
        };
        Console.WriteLine($"{icon} [{p.PercentComplete,3}%] {p.Message}");
    });

    try
    {
        var pipelineResult = await pipeline.RunAsync(url, config, chatClient, audioClient, progress, ct);

        Console.WriteLine();
        Console.WriteLine("─────────────────────────────────────────────");
        Console.WriteLine($"  Summary  : {pipelineResult.MarkdownPath}");
        if (pipelineResult.AudioPath is not null)
            Console.WriteLine($"  Audio    : {pipelineResult.AudioPath}");
        if (pipelineResult.HtmlPagePath is not null)
            Console.WriteLine($"  HTML     : {pipelineResult.HtmlPagePath}");
        if (pipelineResult.GitHubPagesUrl is not null)
            Console.WriteLine($"  Published: {pipelineResult.GitHubPagesUrl}");
        Console.WriteLine("─────────────────────────────────────────────");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"❌ Error: {ex.Message}");
        return 1;
    }
});

// ── Remote mode ───────────────────────────────────────────────────────────────

static async Task<int> RunRemoteAsync(
    string           videoUrl,
    string           serviceUrl,
    PodSlackerConfig config,
    string?          outputFile,
    CancellationToken ct)
{
    Console.WriteLine($"🌐 Remote mode — service: {serviceUrl}");

    // Read the API key from environment if present.
    string? apiKey = Environment.GetEnvironmentVariable("PODSLACKER_API_KEY");

    using var http = new HttpClient { BaseAddress = new Uri(serviceUrl.TrimEnd('/') + "/") };
    if (!string.IsNullOrEmpty(apiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

    // ── POST /api/jobs ────────────────────────────────────────────────────────

    var requestBody = new { video_url = videoUrl, config };
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    Console.WriteLine("⏳ Submitting job…");
    HttpResponseMessage postResponse;
    try
    {
        postResponse = await http.PostAsJsonAsync("api/jobs", requestBody, jsonOptions, ct);
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"❌ Could not reach service at {serviceUrl}: {ex.Message}");
        return 1;
    }

    if (!postResponse.IsSuccessStatusCode)
    {
        string body = await postResponse.Content.ReadAsStringAsync(ct);
        Console.Error.WriteLine($"❌ Server returned {(int)postResponse.StatusCode}: {body}");
        return 1;
    }

    // Parse the job ID from the response.
    using var doc    = await JsonDocument.ParseAsync(
        await postResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    string? jobId    = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
    if (string.IsNullOrEmpty(jobId))
    {
        Console.Error.WriteLine("❌ Could not parse job ID from server response.");
        return 1;
    }

    Console.WriteLine($"✅ Job accepted — ID: {jobId}");
    Console.WriteLine("⏳ Polling for completion…");

    // ── Poll GET /api/jobs/{id} ───────────────────────────────────────────────

    int lastPercent = -1;
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        HttpResponseMessage statusResponse;
        try
        {
            statusResponse = await http.GetAsync($"api/jobs/{jobId}", ct);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"❌ Lost connection to service: {ex.Message}");
            return 1;
        }

        if (!statusResponse.IsSuccessStatusCode)
        {
            string body = await statusResponse.Content.ReadAsStringAsync(ct);
            Console.Error.WriteLine($"❌ Status poll returned {(int)statusResponse.StatusCode}: {body}");
            return 1;
        }

        using var statusDoc = await JsonDocument.ParseAsync(
            await statusResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = statusDoc.RootElement;

        string status  = root.TryGetProperty("status",  out var s) ? s.GetString() ?? "" : "";
        string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        int    percent = root.TryGetProperty("percent", out var p) ? p.GetInt32() : 0;

        if (percent != lastPercent)
        {
            Console.WriteLine($"⏳ [{percent,3}%] {message}");
            lastPercent = percent;
        }

        if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            break;

        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            string error = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Unknown error";
            Console.Error.WriteLine($"❌ Job failed: {error}");
            return 1;
        }
    }

    // ── Download GET /api/jobs/{id}/page ──────────────────────────────────────

    Console.WriteLine("⏳ Downloading HTML page…");
    HttpResponseMessage pageResponse;
    try
    {
        pageResponse = await http.GetAsync($"api/jobs/{jobId}/page", ct);
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"❌ Could not download page: {ex.Message}");
        return 1;
    }

    if (!pageResponse.IsSuccessStatusCode)
    {
        string body = await pageResponse.Content.ReadAsStringAsync(ct);
        Console.Error.WriteLine($"❌ Page download returned {(int)pageResponse.StatusCode}: {body}");
        return 1;
    }

    // Determine output path.
    string savePath = outputFile
        ?? Path.Combine(Directory.GetCurrentDirectory(), $"podslacker_{jobId}.html");
    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

    await using var fileStream = File.OpenWrite(savePath);
    await pageResponse.Content.CopyToAsync(fileStream, ct);

    Console.WriteLine();
    Console.WriteLine("─────────────────────────────────────────────");
    Console.WriteLine($"  HTML     : {savePath}");
    Console.WriteLine($"  Job ID   : {jobId}");
    Console.WriteLine($"  Service  : {serviceUrl}");
    Console.WriteLine("─────────────────────────────────────────────");
    return 0;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static string? FindDefaultConfig()
{
    // Look for podslacker.json next to the executable, then the working directory.
    foreach (string candidate in new[]
    {
        Path.Combine(AppContext.BaseDirectory, "podslacker.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "podslacker.json"),
    })
    {
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

// ── Entry point ───────────────────────────────────────────────────────────────

return await CommandLineParser.Parse(root, args, new ParserConfiguration())
    .InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
