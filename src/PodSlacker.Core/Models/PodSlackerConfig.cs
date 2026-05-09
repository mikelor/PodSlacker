namespace PodSlacker.Core.Models;

/// <summary>
/// All runtime configuration for a PodSlacker run, populated from podslacker.json
/// and/or CLI flags. Mirrors the Python podslacker.json schema exactly so the same
/// config file works for both implementations.
/// </summary>
public sealed record PodSlackerConfig
{
    // ── Output ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Directory where all generated files (markdown, audio, HTML, frames) are written.
    /// May be an absolute path or a path relative to the working directory.
    /// </summary>
    public string OutputDir { get; init; } = "outputs";

    // ── Host names ───────────────────────────────────────────────────────────

    /// <summary>Number of podcast hosts — <c>1</c> for a solo monologue, <c>2</c> for a two-host dialogue.</summary>
    public int    Hosts     { get; init; } = 2;
    /// <summary>Display name for the first (or only) podcast host, used in the script and TTS voice selection.</summary>
    public string Host1Name { get; init; } = "ALEX";
    /// <summary>Display name for the second podcast host; ignored when <see cref="Hosts"/> is <c>1</c>.</summary>
    public string Host2Name { get; init; } = "JORDAN";

    // ── Flags ────────────────────────────────────────────────────────────────

    /// <summary>When <see langword="true"/>, skips TTS synthesis and produces no audio file.</summary>
    public bool NoAudio      { get; init; } = false;
    /// <summary>
    /// When <see langword="true"/>, reuses an existing <c>*_summary.md</c> file in
    /// <see cref="OutputDir"/> instead of calling the LLM again.
    /// </summary>
    public bool ReuseSummary { get; init; } = false;
    /// <summary>When <see langword="true"/>, skips HTML page generation.</summary>
    public bool NoPage       { get; init; } = false;
    /// <summary>Number of key-moment frames to capture from the video. Set to <c>0</c> to disable frame capture.</summary>
    public int  NumFrames    { get; init; } = 6;

    // ── YouTube access ───────────────────────────────────────────────────────

    /// <summary>
    /// Optional path to a Netscape-format <c>cookies.txt</c> file exported from a browser.
    /// Passed to YoutubeExplode and all HTTP fallback tiers to access age-restricted
    /// or member-only videos.
    /// </summary>
    public string? Cookies  { get; init; }
    /// <summary>
    /// Optional proxy URL (e.g. <c>http://user:pass@host:port</c>) forwarded to
    /// YoutubeExplode and all internal HTTP clients.
    /// </summary>
    public string? Proxy    { get; init; }

    // ── Base LLM provider (used as fallback for all steps) ───────────────────

    /// <summary>LLM model name sent in every <c>ChatOptions.ModelId</c> field (e.g. <c>openrouter/auto:free</c> or <c>gpt-4o</c>).</summary>
    public string  LlmModel      { get; init; } = "openrouter/auto:free";
    /// <summary>
    /// Base URL for the LLM API endpoint. Defaults to OpenRouter; set to <see langword="null"/>
    /// to use the default OpenAI endpoint, or supply any other OpenAI-compatible URL.
    /// </summary>
    public string? LlmBaseUrl    { get; init; } = "https://openrouter.ai/api/v1";
    /// <summary>Name of the environment variable that holds the LLM API key (e.g. <c>OPENROUTER_API_KEY</c> or <c>OPENAI_API_KEY</c>).</summary>
    public string  LlmApiKeyEnv  { get; init; } = "OPENROUTER_API_KEY";

    // ── Per-step LLM overrides (null = use base LLM settings) ────────────────

    /// <summary>LLM model override for the summarisation step; falls back to <see cref="LlmModel"/> when <see langword="null"/>.</summary>
    public string? SummaryModel     { get; init; }
    /// <summary>LLM base URL override for the summarisation step; falls back to <see cref="LlmBaseUrl"/> when <see langword="null"/>.</summary>
    public string? SummaryBaseUrl   { get; init; }
    /// <summary>API key environment-variable name override for the summarisation step; falls back to <see cref="LlmApiKeyEnv"/> when <see langword="null"/>.</summary>
    public string? SummaryApiKeyEnv { get; init; }

    /// <summary>LLM model override for the script-generation step; falls back to <see cref="LlmModel"/> when <see langword="null"/>.</summary>
    public string? ScriptModel     { get; init; }
    /// <summary>LLM base URL override for the script-generation step; falls back to <see cref="LlmBaseUrl"/> when <see langword="null"/>.</summary>
    public string? ScriptBaseUrl   { get; init; }
    /// <summary>API key environment-variable name override for the script-generation step; falls back to <see cref="LlmApiKeyEnv"/> when <see langword="null"/>.</summary>
    public string? ScriptApiKeyEnv { get; init; }

    /// <summary>LLM model override for the key-moments identification step; falls back to <see cref="LlmModel"/> when <see langword="null"/>.</summary>
    public string? KeyMomentsModel     { get; init; }
    /// <summary>LLM base URL override for the key-moments identification step; falls back to <see cref="LlmBaseUrl"/> when <see langword="null"/>.</summary>
    public string? KeyMomentsBaseUrl   { get; init; }
    /// <summary>API key environment-variable name override for the key-moments identification step; falls back to <see cref="LlmApiKeyEnv"/> when <see langword="null"/>.</summary>
    public string? KeyMomentsApiKeyEnv { get; init; }

    // ── TTS ──────────────────────────────────────────────────────────────────

    /// <summary>TTS backend to use: <c>"kokoro"</c> for local Kokoro inference (default), or <c>"openai"</c> for the OpenAI TTS API.</summary>
    public string  TtsEngine     { get; init; } = "kokoro";   // "kokoro" | "openai"
    /// <summary>OpenAI TTS model name (e.g. <c>tts-1</c> or <c>tts-1-hd</c>). Ignored when <see cref="TtsEngine"/> is <c>"kokoro"</c>.</summary>
    public string  TtsModel      { get; init; } = "tts-1";
    /// <summary>Name of the environment variable that holds the TTS API key. Defaults to <c>OPENAI_API_KEY</c>.</summary>
    public string  TtsApiKeyEnv  { get; init; } = "OPENAI_API_KEY";
    /// <summary>
    /// Voice name for the first host, interpreted by whichever TTS engine is active.
    /// Kokoro examples: <c>af_heart</c>, <c>bf_emma</c>.
    /// OpenAI examples: <c>nova</c>, <c>shimmer</c>, <c>fable</c>.
    /// Defaults to <c>af_heart</c> (Kokoro) since Kokoro is the default engine.
    /// </summary>
    public string  VoiceHost1    { get; init; } = "af_heart";
    /// <summary>
    /// Voice name for the second host, interpreted by whichever TTS engine is active.
    /// Kokoro examples: <c>am_michael</c>, <c>bm_george</c>.
    /// OpenAI examples: <c>onyx</c>, <c>echo</c>, <c>alloy</c>.
    /// Defaults to <c>am_michael</c> (Kokoro) since Kokoro is the default engine.
    /// </summary>
    public string  VoiceHost2    { get; init; } = "am_michael";

    // ── Prompt overrides ─────────────────────────────────────────────────────

    /// <summary>Path to a custom plain-text summary prompt file; uses the embedded default when <see langword="null"/>.</summary>
    public string? SummaryPrompt    { get; init; }
    /// <summary>Path to a custom plain-text dialogue (two-host) prompt file; uses the embedded default when <see langword="null"/>.</summary>
    public string? DialoguePrompt   { get; init; }
    /// <summary>Path to a custom plain-text monologue (solo-host) prompt file; uses the embedded default when <see langword="null"/>.</summary>
    public string? MonologuePrompt  { get; init; }
    /// <summary>Path to a custom plain-text key-moments prompt file; uses the embedded default when <see langword="null"/>.</summary>
    public string? KeyMomentsPrompt { get; init; }

    // ── GitHub Pages publishing ───────────────────────────────────────────────

    /// <summary>When <see langword="true"/>, publishes the generated HTML page to GitHub Pages after the run completes.</summary>
    public bool    PublishGithub    { get; init; } = false;
    /// <summary>Name of the GitHub repository to publish to (e.g. <c>podslacker-pages</c>). Created automatically if it does not exist.</summary>
    public string  GithubRepo       { get; init; } = "podslacker-pages";
    /// <summary>Name of the environment variable that holds a GitHub Personal Access Token with <c>repo</c> and <c>pages</c> scopes.</summary>
    public string  GithubTokenEnv   { get; init; } = "GITHUB_TOKEN";
    /// <summary>
    /// Optional literal GitHub Personal Access Token. When non-null this takes precedence over
    /// <see cref="GithubTokenEnv"/>, so callers (e.g. the web UI) can supply a token directly
    /// without requiring it to be set as a server-side environment variable.
    /// </summary>
    public string? GithubTokenValue { get; init; }
    /// <summary>Git branch used as the GitHub Pages source (e.g. <c>gh-pages</c>). Created automatically if it does not exist.</summary>
    public string  GithubBranch     { get; init; } = "gh-pages";
    /// <summary>
    /// Optional subfolder within the GitHub Pages repository where published files are placed.
    /// For example <c>"google-cloud-next"</c> causes files to be committed as
    /// <c>google-cloud-next/video_page.html</c> rather than at the repo root.
    /// Leave empty (default) to publish directly to the root of the branch.
    /// </summary>
    public string  GithubFolder     { get; init; } = "";
    /// <summary>
    /// When <see langword="true"/>, audio and images are base64-encoded directly into the
    /// published HTML so it works as a single self-contained file.
    /// When <see langword="false"/> (default), audio and images are uploaded as separate files
    /// and referenced via relative URLs — this produces a much smaller HTML file and is the
    /// recommended mode for GitHub Pages.
    /// </summary>
    public bool    GithubEmbedAssets { get; init; } = false;
}
