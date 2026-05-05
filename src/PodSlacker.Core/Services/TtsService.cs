using OpenAI.Audio;
using Microsoft.Extensions.Logging;
using PodSlacker.Core.Audio;
using PodSlacker.Core.Models;

namespace PodSlacker.Core.Services;

/// <summary>
/// Generates TTS audio for each dialogue segment and stitches the segments
/// into a single MP3 file using raw byte concatenation (no ffmpeg required).
///
/// Mirrors the Python generate_audio() function exactly, including:
///   - Format-matched silence via SilenceGenerator (built lazily from first segment).
///   - Xing/LAME VBR frame stripping via Mp3FrameParser.StripVbrInfoFrames().
///   - OpenRouter support: pass an OpenAIClient pointed at the OpenRouter base URL.
/// </summary>
public sealed class TtsService(ILogger<TtsService> logger)
{
    private const int SilenceFrameCount = 20;   // ~0.5 s at typical bitrates

    /// <summary>
    /// Generates one MP3 per dialogue segment via the TTS API, stitches them
    /// together with format-matched silence padding, and writes the result to
    /// <paramref name="outputPath"/>.
    /// </summary>
    public async Task GenerateAudioAsync(
        AudioClient             client,
        List<DialogueSegment>   segments,
        string                  outputPath,
        string                  voiceHost1  = "onyx",
        string                  voiceHost2  = "nova",
        string                  ttsModel    = "tts-1",
        string                  host1Name   = "ALEX",
        string                  host2Name   = "JORDAN",
        CancellationToken       ct          = default)
    {
        var voiceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [host1Name] = voiceHost1,
            [host2Name] = voiceHost2,
        };

        int total = segments.Count;
        var chunks = new List<byte[]>(total * 2 - 1);
        byte[]? pauseBytes = null;

        for (int i = 0; i < total; i++)
        {
            var (speaker, text) = segments[i];
            string preview = text.Length > 60 ? text[..60] + "..." : text;
            logger.LogInformation("Segment {Idx}/{Total}  [{Speaker}]  \"{Preview}\"",
                i + 1, total, speaker, preview);

            if (!voiceMap.TryGetValue(speaker, out string? voice))
                voice = voiceHost1;

            // Call the TTS API. No SpeechGenerationOptions needed — omitting
            // it uses the server default speed (1.0), and SpeedRatio was renamed
            // in OpenAI SDK 2.10.0. GeneratedSpeechVoice has an implicit string
            // operator so the cast from string is handled automatically.
            var ttsResponse = await client.GenerateSpeechAsync(
                text,
                (GeneratedSpeechVoice)voice,
                cancellationToken: ct);

            byte[] segBytes = ttsResponse.Value.ToArray();

            // Build format-matched silence from the very first segment.
            pauseBytes ??= SilenceGenerator.Build(segBytes, SilenceFrameCount);

            chunks.Add(segBytes);
            if (i < total - 1)
                chunks.Add(pauseBytes);
        }

        // Write stitched audio.
        int totalBytes = chunks.Sum(c => c.Length);
        byte[] combined = new byte[totalBytes];
        int offset = 0;
        foreach (byte[] chunk in chunks)
        {
            chunk.CopyTo(combined, offset);
            offset += chunk.Length;
        }

        await File.WriteAllBytesAsync(outputPath, combined, ct);
        logger.LogInformation("Audio written to {Path}", outputPath);
    }
}
