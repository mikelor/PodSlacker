using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using PodSlacker.Core.Models;
using System.ClientModel;

namespace PodSlacker.Core.Pipeline;

/// <summary>
/// Shared factory for building the LLM and TTS clients from a
/// <see cref="PodSlackerConfig"/>.  Used by both the CLI and the API service
/// so the construction logic stays in one place.
/// </summary>
public static class PipelineClientFactory
{
    // Optional logger; callers can supply one for diagnostic output.
    private static ILogger? _logger;

    /// <summary>
    /// Registers a logger so the factory can emit diagnostic output about
    /// which URL, model, and API key (prefix only) it is using.
    /// Call once at startup — e.g. from <c>Program.cs</c>.
    /// </summary>
    public static void UseLogger(ILogger logger) => _logger = logger;

    /// <summary>
    /// Builds an <see cref="IChatClient"/> from the base LLM settings in
    /// <paramref name="config"/>.  The API key must be present in the environment
    /// variable named by <see cref="PodSlackerConfig.LlmApiKeyEnv"/>.
    /// </summary>
    /// <param name="config">Runtime configuration.</param>
    /// <returns>An <see cref="IChatClient"/> ready for chat completions.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the required API key environment variable is not set.
    /// </exception>
    public static IChatClient CreateChatClient(PodSlackerConfig config)
    {
        string apiKey = RequireEnv(config.LlmApiKeyEnv,
            $"LLM API key (set the '{config.LlmApiKeyEnv}' environment variable)");

        string endpoint = config.LlmBaseUrl ?? "(default OpenAI)";
        string keyHint  = apiKey.Length > 8
            ? $"{apiKey[..8]}…({apiKey.Length} chars)"
            : "(too short — likely wrong)";

        _logger?.LogInformation(
            "LLM client → endpoint: {Endpoint} | model: {Model} | key env: {KeyEnv} | key: {KeyHint}",
            endpoint, config.LlmModel, config.LlmApiKeyEnv, keyHint);

        var openAiClient = config.LlmBaseUrl is not null
            ? new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(config.LlmBaseUrl) })
            : new OpenAIClient(new ApiKeyCredential(apiKey));

        return openAiClient.GetChatClient(config.LlmModel).AsIChatClient();
    }

    /// <summary>
    /// Builds an <see cref="AudioClient"/> for OpenAI TTS synthesis, or returns
    /// <see langword="null"/> when audio is disabled or the Kokoro engine is active.
    /// </summary>
    /// <param name="config">Runtime configuration.</param>
    /// <returns>
    /// An <see cref="AudioClient"/> when <see cref="PodSlackerConfig.TtsEngine"/> is
    /// <c>"openai"</c> and <see cref="PodSlackerConfig.NoAudio"/> is <see langword="false"/>;
    /// otherwise <see langword="null"/>.
    /// </returns>
    public static AudioClient? CreateAudioClient(PodSlackerConfig config)
    {
        bool useOpenAiTts = !config.NoAudio &&
                            config.TtsEngine.Equals("openai", StringComparison.OrdinalIgnoreCase);
        if (!useOpenAiTts)
            return null;

        string ttsApiKey = RequireEnv(config.TtsApiKeyEnv,
            $"TTS API key (set the '{config.TtsApiKeyEnv}' environment variable)");

        // Reuse the base LLM client when the credentials are the same and there
        // is no custom base URL (i.e. both point at vanilla OpenAI).
        bool sameCredentials = config.TtsApiKeyEnv == config.LlmApiKeyEnv
                            && config.LlmBaseUrl is null;

        OpenAIClient ttsOpenAiClient;
        if (sameCredentials)
        {
            string llmApiKey = Environment.GetEnvironmentVariable(config.LlmApiKeyEnv) ?? ttsApiKey;
            ttsOpenAiClient = new OpenAIClient(new ApiKeyCredential(llmApiKey));
        }
        else
        {
            ttsOpenAiClient = new OpenAIClient(new ApiKeyCredential(ttsApiKey));
        }

        return ttsOpenAiClient.GetAudioClient(config.TtsModel);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string RequireEnv(string varName, string description)
    {
        string? value = Environment.GetEnvironmentVariable(varName);
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException(
                $"Required environment variable '{varName}' is not set. " +
                $"It is needed for: {description}");
        return value;
    }
}
