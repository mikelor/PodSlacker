using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PodSlacker.Core.Models;

namespace PodSlacker.Core.Services;

/// <summary>
/// Seeks to key timestamps in a video stream and saves JPEG frames.
/// Mirrors the Python capture_frames() and get_frame_captions() functions.
///
/// Uses OpenCvSharp4 which has a one-to-one API with Python's cv2 — VideoCapture,
/// CAP_PROP_POS_MSEC, ImWrite are the same class/method names.
/// </summary>
public sealed class FrameCaptureService(ILogger<FrameCaptureService> logger)
{
    /// <summary>
    /// Opens the video at <paramref name="streamUrl"/> (a direct HTTP URL obtained from
    /// YoutubeExplode's stream manifest), seeks to each timestamp, and saves a JPEG frame.
    ///
    /// Returns a list of (filePath, timestampSeconds) pairs for successfully saved
    /// frames. Skipped frames are omitted.
    /// </summary>
    public List<CapturedFrame> CaptureFrames(
        string        streamUrl,
        List<double>  timestamps,
        string        outputDir,
        string        videoId)
    {
        using var cap = new VideoCapture(streamUrl);
        if (!cap.IsOpened())
            throw new InvalidOperationException(
                "OpenCV could not open the video stream. " +
                "The stream URL may have expired — try running again.");

        var saved = new List<CapturedFrame>();
        int total = timestamps.Count;

        for (int i = 0; i < total; i++)
        {
            double ts = timestamps[i];
            cap.Set(VideoCaptureProperties.PosMsec, ts * 1000.0);

            using var frame = new Mat();
            bool ok = cap.Read(frame);
            if (!ok || frame.Empty())
            {
                logger.LogWarning("Frame {Idx}/{Total} at {Ts:F1}s — could not read, skipping.",
                    i + 1, total, ts);
                continue;
            }

            string framePath = Path.Combine(outputDir, $"{videoId}_frame_{i + 1:D2}.jpg");
            bool written = Cv2.ImWrite(framePath, frame,
                [new ImageEncodingParam(ImwriteFlags.JpegQuality, 92)]);

            if (written)
            {
                int m = (int)(ts / 60), s = (int)(ts % 60);
                logger.LogInformation("Frame {Idx}/{Total}  [{M:D2}:{S:D2}]  → {Name}",
                    i + 1, total, m, s, Path.GetFileName(framePath));
                saved.Add(new CapturedFrame(framePath, ts));
            }
            else
            {
                logger.LogWarning("Frame {Idx}/{Total} at {Ts:F1}s — failed to write JPEG.", i + 1, total, ts);
            }
        }

        return saved;
    }

    /// <summary>
    /// Returns a short caption for each timestamp drawn from nearby transcript text.
    /// Mirrors the Python get_frame_captions() function.
    /// </summary>
    public static List<string> GetFrameCaptions(
        List<TimedTranscriptEntry> timedEntries,
        List<double>               timestamps,
        double                     windowSeconds = 20.0,
        int                        maxChars      = 110)
    {
        var captions = new List<string>(timestamps.Count);

        foreach (double ts in timestamps)
        {
            var nearby = timedEntries
                .Where(e => Math.Abs(e.StartSeconds - ts) <= windowSeconds)
                .OrderBy(e => Math.Abs(e.StartSeconds - ts))
                .ToList();

            if (nearby.Count == 0 && timedEntries.Count > 0)
            {
                // Nothing within window — use the globally closest entry.
                var nearest = timedEntries.MinBy(e => Math.Abs(e.StartSeconds - ts))!;
                nearby = [nearest];
            }

            if (nearby.Count > 0)
            {
                string combined = string.Join(' ', nearby.Select(e => e.Text));
                combined = string.Join(' ', combined.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                if (combined.Length > maxChars)
                {
                    int cut = combined.LastIndexOf(' ', maxChars);
                    combined = (cut > 0 ? combined[..cut] : combined[..maxChars])
                        .TrimEnd('.', ',', ';', ':') + "…";
                }
                captions.Add(combined);
            }
            else
            {
                captions.Add(string.Empty);
            }
        }

        return captions;
    }
}
