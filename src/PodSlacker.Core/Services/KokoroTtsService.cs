using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using KokoroSharp.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PodSlacker.Core.Models;

namespace PodSlacker.Core.Services;

/// <summary>
/// Generates TTS audio for each dialogue segment using the Kokoro 82M ONNX model
/// via KokoroSharp, and stitches the segments into a single WAV file.
///
/// The model (~320 MB, <c>kokoro.onnx</c>) is downloaded automatically on first
/// use to the application base directory and reused on subsequent runs.
/// All Kokoro voices are bundled in the KokoroSharp.CPU NuGet package and are
/// available immediately after installation — no separate download required.
///
/// Audio output format: 24 kHz / 16-bit / mono WAV.
/// This is natively supported by the PodSlacker HTML page audio player.
/// </summary>
public sealed class KokoroTtsService(ILogger<KokoroTtsService> logger)
{
    // ── Constants ────────────────────────────────────────────────────────────

    private const int    SilenceMs        = 500;      // pause between segments (ms)
    private const int    SampleRate       = 24_000;   // Kokoro native output rate
    private const int    BitDepth         = 16;       // 16-bit signed PCM
    private const int    Channels         = 1;        // mono
    private const int    BytesPerSample   = BitDepth / 8 * Channels;
    private const string ModelFileName    = "kokoro.onnx";
    private const string ModelDownloadUrl =
        "https://github.com/taylorchu/kokoro-onnx/releases/download/v0.2.0/kokoro.onnx";

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Synthesises each dialogue segment using the Kokoro ONNX model, stitches
    /// the segments together with brief silence gaps, and writes the result as
    /// a standard WAV file.  The model is downloaded automatically on the first
    /// call (~320 MB, one-time).
    /// </summary>
    /// <param name="segments">Ordered list of speaker turns to synthesise.</param>
    /// <param name="outputPath">Destination path for the output <c>.wav</c> file.</param>
    /// <param name="voiceHost1">
    /// Kokoro voice name for the first host (e.g. <c>am_michael</c>).
    /// Full list: run <c>KokoroVoiceManager.Voices</c> after loading.
    /// </param>
    /// <param name="voiceHost2">
    /// Kokoro voice name for the second host (e.g. <c>af_heart</c>).
    /// </param>
    /// <param name="host1Name">Speaker label in <paramref name="segments"/> for host 1.</param>
    /// <param name="host2Name">Speaker label in <paramref name="segments"/> for host 2.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task GenerateAudioAsync(
        List<DialogueSegment> segments,
        string                outputPath,
        string                voiceHost1 = "am_michael",
        string                voiceHost2 = "af_heart",
        string                host1Name  = "MIKE",
        string                host2Name  = "JORDAN",
        CancellationToken     ct         = default)
    {
        string modelPath = await EnsureModelAsync(ct);

        // KokoroWavSynthesizer owns a background ONNX inference thread.
        // Dispose after synthesis to release it cleanly.
        using var synthesizer = new KokoroWavSynthesizer(modelPath, new SessionOptions());

        // KokoroVoiceManager.GetVoice auto-loads the bundled voices/ folder on
        // first call via AppDomain.CurrentDomain.BaseDirectory.
        var voiceMap = new Dictionary<string, KokoroVoice>(StringComparer.OrdinalIgnoreCase)
        {
            [host1Name] = LoadVoice(voiceHost1, "am_michael"),
            [host2Name] = LoadVoice(voiceHost2, "af_heart"),
        };
        var fallback = voiceMap.Values.First();

        int total = segments.Count;
        var pcmChunks = new List<byte[]>(total);

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (speaker, text) = segments[i];
            string preview = text.Length > 60 ? text[..60] + "…" : text;
            logger.LogInformation("Segment {Idx}/{Total}  [{Speaker}]  \"{Preview}\"",
                i + 1, total, speaker, preview);

            var voice = voiceMap.GetValueOrDefault(speaker, fallback);

            // SynthesizeAsync returns raw 16-bit PCM bytes — no WAV header.
            byte[] pcm = await synthesizer.SynthesizeAsync(text, voice, new KokoroTTSPipelineConfig());
            pcmChunks.Add(pcm);
        }

        logger.LogInformation("Stitching {Count} segments → {Path}", pcmChunks.Count, outputPath);
        WriteWavFile(pcmChunks, outputPath);
        logger.LogInformation("Audio written to {Path}", outputPath);
    }

    // ── Model management ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the path to the Kokoro ONNX model, downloading it the first time.
    /// The model is stored in <see cref="AppContext.BaseDirectory"/> alongside the
    /// executable so all future runs find it immediately.
    /// </summary>
    private async Task<string> EnsureModelAsync(CancellationToken ct)
    {
        string modelPath = Path.Combine(AppContext.BaseDirectory, ModelFileName);

        if (File.Exists(modelPath))
        {
            logger.LogInformation("Kokoro model found: {Path}", modelPath);
            return modelPath;
        }

        logger.LogInformation(
            "Kokoro model not found. Downloading ~320 MB from GitHub — this happens once.");

        string tempPath = modelPath + ".download";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var resp = await http.GetAsync(
                ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;
            int  lastBucket = -1;
            var  buffer     = new byte[81_920];

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.OpenWrite(tempPath);

            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;

                if (totalBytes > 0)
                {
                    int pct    = (int)(downloaded * 100 / totalBytes);
                    int bucket = pct / 10;
                    if (bucket != lastBucket)
                    {
                        logger.LogInformation(
                            "Downloading Kokoro model… {Pct}%  ({Done:F0} / {Total:F0} MB)",
                            pct,
                            downloaded  / 1_048_576.0,
                            totalBytes  / 1_048_576.0);
                        lastBucket = bucket;
                    }
                }
            }
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }

        File.Move(tempPath, modelPath, overwrite: true);
        logger.LogInformation("Kokoro model ready at {Path}", modelPath);
        return modelPath;
    }

    // ── Voice helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a Kokoro voice by name, falling back to <paramref name="fallbackName"/>
    /// with a warning if the requested voice is not found in the bundled voice pack.
    /// </summary>
    private KokoroVoice LoadVoice(string name, string fallbackName)
    {
        try
        {
            return KokoroVoiceManager.GetVoice(name);
        }
        catch
        {
            logger.LogWarning(
                "Kokoro voice '{Name}' not found in the bundled voice pack. " +
                "Falling back to '{Fallback}'. " +
                "Available voices: {Voices}",
                name, fallbackName,
                string.Join(", ", KokoroVoiceManager.Voices.Select(v => v.Name)));

            return KokoroVoiceManager.GetVoice(fallbackName);
        }
    }

    // ── WAV stitching ─────────────────────────────────────────────────────────

    /// <summary>
    /// Concatenates raw PCM chunks with <see cref="SilenceMs"/> ms of silence
    /// between each segment and writes a standard RIFF/WAVE file.
    /// </summary>
    private static void WriteWavFile(List<byte[]> pcmChunks, string outputPath)
    {
        int silenceBytes = SampleRate * SilenceMs / 1000 * BytesPerSample;
        int totalPcm     = pcmChunks.Sum(c => c.Length)
                         + silenceBytes * Math.Max(0, pcmChunks.Count - 1);

        using var fs = File.OpenWrite(outputPath);
        using var bw = new BinaryWriter(fs);

        WriteWavHeader(bw, SampleRate, Channels, BitDepth, totalPcm);

        for (int i = 0; i < pcmChunks.Count; i++)
        {
            bw.Write(pcmChunks[i]);
            if (i < pcmChunks.Count - 1)
                bw.Write(new byte[silenceBytes]);   // inter-segment silence
        }
    }

    /// <summary>Writes a 44-byte standard RIFF/WAVE PCM file header.</summary>
    private static void WriteWavHeader(
        BinaryWriter bw, int sampleRate, int channels, int bitsPerSample, int dataSize)
    {
        int byteRate   = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels   * (bitsPerSample / 8);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataSize);            // RIFF chunk size
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);                       // PCM subchunk1 size
        bw.Write((short)1);                 // AudioFormat = PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);
    }
}
