using System.CommandLine;
using System.CommandLine.Parsing;
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

// Mode
var serverOpt = new Option<string?>("--server")
    { Description = "Submit the job to a running PodSlacker web service instead of running locally. " +
                    "Example: --server http://localhost:5000" };

// YouTube access
var cookiesOpt = new Option<string?>("--cookies")  { Description = "Path to a Netscape-format cookies.txt file" };
var proxyOpt   = new Option<string?>("--proxy")    { Description = "Proxy URL, e.g. http://user:pass@host:port" };

// Behaviour flags — bool flags default to false, so no special handling needed.
// Int/string flags are nullable so a missing CLI flag returns null and the
// config file value flows through; PodSlackerConfig holds the final defaults.
var noAudioOpt      = new Option<bool>("--no-audio")       { Description = "Skip audio generation" };
var noPageOpt       = new Option<bool>("--no-page")        { Description = "Skip HTML page generation" };
var reuseSummaryOpt = new Option<bool>("--reuse-summary")  { Description = "Reuse existing summary markdown if present" };
var numFramesOpt    = new Option<int?>("--num-frames")     { Description = "Number of key-moment frames to capture (default: 5)" };
var hostsOpt        = new Option<int?>("--hosts")          { Description = "Number of podcast hosts: 1 or 2 (default: 2)" };

// Host names
var host1NameOpt = new Option<string?>("--host1-name") { Description = "Name for host 1 (default: ALEX)" };
var host2NameOpt = new Option<string?>("--host2-name") { Description = "Name for host 2 (default: JORDAN)" };

// Base LLM
var llmBaseUrlOpt   = new Option<string?>("--llm-base-url")   { Description = "LLM API base URL (default: OpenAI)" };
var llmModelOpt     = new Option<string?>("--llm-model")      { Description = "LLM model name (default: gpt-4o)" };
var llmApiKeyEnvOpt = new Option<string?>("--llm-api-key-env"){ Description = "Env var for LLM API key (default: OPENAI_API_KEY)" };

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

// Add all options to the generate command
foreach (var opt in new Option[]
{
    outputDirOpt, serverOpt, cookiesOpt, proxyOpt, noAudioOpt, noPageOpt,
    reuseSummaryOpt, numFramesOpt, hostsOpt, host1NameOpt, host2NameOpt,
    llmBaseUrlOpt, llmModelOpt, llmApiKeyEnvOpt,
    summaryModelOpt, summaryBaseUrlOpt, summaryApiKeyOpt,
    scriptModelOpt, scriptBaseUrlOpt, scriptApiKeyOpt,
    kmModelOpt, kmBaseUrlOpt, kmApiKeyOpt,
    ttsEngineOpt, ttsModelOpt, ttsApiKeyOpt, voice1Opt, voice2Opt,
    summaryPromptOpt, dialoguePromptOpt, monologuePromptOpt, kmPromptOpt,
    publishGhOpt, ghRepoOpt, ghTokenOpt, ghBranchOpt, configOpt,
})
{
    generateCmd.Add(opt);
}

// ── Handler ───────────────────────────────────────────────────────────────────

generateCmd.SetAction(async (ParseResult result, CancellationToken ct) =>
{
    string  url       = result.GetValue(urlArg)!;
    string? serverUrl = result.GetValue(serverOpt);

    // If --server is specified, delegate to the remote API.
    if (serverUrl is not null)
        return await RunRemoteAsync(url, serverUrl);

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

    string llmApiKey = RequireEnv(config.LlmApiKeyEnv, "LLM API key");
    var openAiClient = config.LlmBaseUrl is not null
        ? new OpenAIClient(
            new ApiKeyCredential(llmApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(config.LlmBaseUrl) })
        : new OpenAIClient(new ApiKeyCredential(llmApiKey));

    IChatClient chatClient = openAiClient.GetChatClient(config.LlmModel).AsIChatClient();

    // ── Build TTS client (OpenAI only; Kokoro runs locally without a client) ─────

    AudioClient? audioClient = null;
    bool useOpenAiTts = !config.NoAudio &&
                        config.TtsEngine.Equals("openai", StringComparison.OrdinalIgnoreCase);
    if (useOpenAiTts)
    {
        string ttsApiKey = RequireEnv(config.TtsApiKeyEnv, "TTS API key");
        var ttsOpenAiClient = config.TtsApiKeyEnv == config.LlmApiKeyEnv && config.LlmBaseUrl is null
            ? openAiClient
            : new OpenAIClient(new ApiKeyCredential(ttsApiKey));
        audioClient = ttsOpenAiClient.GetAudioClient(config.TtsModel);
    }

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

// ── Remote mode stub ─────────────────────────────────────────────────────────

static Task<int> RunRemoteAsync(string videoUrl, string serverUrl)
{
    // TODO (Phase 4): POST job to PodSlacker.Api and stream SSE progress.
    Console.WriteLine($"🌐 Remote mode: submitting to {serverUrl}");
    Console.WriteLine("   (Web service support not yet implemented — run without --server for local mode)");
    return Task.FromResult(1);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static string RequireEnv(string varName, string label)
{
    string? value = Environment.GetEnvironmentVariable(varName);
    if (string.IsNullOrEmpty(value))
    {
        Console.Error.WriteLine($"Error: {label} environment variable '{varName}' is not set.");
        Environment.Exit(1);
    }
    return value!;
}

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

// ── 'status' subcommand (remote job polling) ──────────────────────────────────

var statusCmd       = new Command("status", "Check the status of a remote job");
var jobIdArg        = new Argument<string>("job-id") { Description = "Job ID returned by the remote server" };
var statusServerOpt = new Option<string>("--server")
    { Description = "PodSlacker web service URL", Required = true };
statusCmd.Add(jobIdArg);
statusCmd.Add(statusServerOpt);
statusCmd.SetAction(async (ParseResult result, CancellationToken ct) =>
{
    string jobId  = result.GetValue(jobIdArg)!;
    string server = result.GetValue(statusServerOpt)!;
    // TODO (Phase 4): GET /api/jobs/{jobId} from the web service.
    Console.WriteLine($"🌐 Checking job {jobId} on {server}");
    Console.WriteLine("   (Remote status checking not yet implemented)");
    await Task.CompletedTask;
    return 1;
});
root.Add(statusCmd);

// ── Entry point ───────────────────────────────────────────────────────────────

return await CommandLineParser.Parse(root, args, new ParserConfiguration())
    .InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
