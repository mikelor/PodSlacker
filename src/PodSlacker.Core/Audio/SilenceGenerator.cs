using System.Buffers.Binary;

namespace PodSlacker.Core.Audio;

/// <summary>
/// Generates MPEG Layer-III silence frames whose format (version, bitrate,
/// sample rate, channel mode) exactly matches a reference MP3 segment.
///
/// Mirrors the Python _make_silence_frames() function. The key insight is that
/// mixing MPEG1 silence with MPEG2 speech (or stereo silence with mono speech)
/// causes browser audio pipelines to stop decoding mid-stream. Auto-detecting
/// the TTS output format and generating matching silence avoids this entirely.
/// </summary>
public static class SilenceGenerator
{
    // Fallback: MPEG1, stereo, 128 kbps, 44100 Hz — 417 bytes/frame.
    private static readonly byte[] FallbackHeader = [0xFF, 0xFB, 0x90, 0x00];
    private const int FallbackFrameSize = 417;

    /// <summary>
    /// Builds <paramref name="frameCount"/> silent frames whose MPEG format
    /// matches the first valid Layer-III frame found in <paramref name="reference"/>.
    /// Falls back to MPEG1 stereo 128 kbps 44100 Hz if no valid frame is found.
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> reference, int frameCount)
    {
        byte[] hdrBytes = FallbackHeader;
        int frameSize   = FallbackFrameSize;

        int scanLimit = Math.Min(reference.Length - 4, 4096);
        for (int i = 0; i < scanLimit; i++)
        {
            if (reference[i] != 0xFF || (reference[i + 1] & 0xE0) != 0xE0)
                continue;

            int? fs = Mp3FrameParser.FrameSize(reference, i);
            if (fs is null || fs < 10)
                continue;

            // Clear the padding bit so our fixed-size frame is always exactly fs bytes.
            uint hdr         = BinaryPrimitives.ReadUInt32BigEndian(reference[i..]);
            uint cleanHdr    = hdr & ~(1u << 9);
            hdrBytes         = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(hdrBytes, cleanHdr);

            frameSize = Mp3FrameParser.FrameSize(hdrBytes, 0) ?? fs.Value;
            break;
        }

        // A silent frame is just the header + zero-filled payload.
        byte[] silentFrame = new byte[frameSize];
        hdrBytes.CopyTo(silentFrame, 0);

        byte[] result = new byte[frameSize * frameCount];
        for (int i = 0; i < frameCount; i++)
            silentFrame.CopyTo(result, i * frameSize);

        return result;
    }
}
