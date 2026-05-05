using System.Buffers.Binary;

namespace PodSlacker.Core.Audio;

/// <summary>
/// Low-level MPEG Layer-III frame header parser.
///
/// Mirrors the Python _mp3_frame_size() and _strip_xing_header() functions exactly,
/// using Span&lt;byte&gt; and BinaryPrimitives for zero-allocation byte scanning.
///
/// Critical correctness note (same as the Python port):
///   MPEG1  → 1152 samples/frame → multiplier 144 (= 1152 / 8)
///   MPEG2/2.5 → 576 samples/frame → multiplier  72 (=  576 / 8)
/// Using 144 for MPEG2 streams causes frame-size over-reads and breaks
/// browser audio pipelines — this was the root bug fixed in the Python version.
/// </summary>
public static class Mp3FrameParser
{
    // Bitrate tables (kbps) indexed by the 4-bit br_idx field.
    private static ReadOnlySpan<int> BitratesV1 =>
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
    private static ReadOnlySpan<int> BitratesV2 =>
        [0,  8, 16, 24, 32, 40, 48, 56,  64,  80,  96, 112, 128, 144, 160, 0];

    // Sample-rate tables indexed by (version, sr_idx).
    private static ReadOnlySpan<int> SampleRatesV1  => [44100, 48000, 32000, 0];
    private static ReadOnlySpan<int> SampleRatesV2  => [22050, 24000, 16000, 0];
    private static ReadOnlySpan<int> SampleRatesV25 => [11025, 12000,  8000, 0];

    /// <summary>
    /// Returns the byte length of the MPEG Layer-III frame whose 4-byte header
    /// starts at <paramref name="data"/>[<paramref name="offset"/>], or null if
    /// the header is not a valid Layer-III frame.
    /// </summary>
    public static int? FrameSize(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (data.Length - offset < 4)
            return null;

        uint hdr     = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        int  version = (int)(hdr >> 19) & 0x3;
        int  layer   = (int)(hdr >> 17) & 0x3;

        if (layer != 1)         // must be Layer III (binary 01 = layer 3)
            return null;

        int brIdx   = (int)(hdr >> 12) & 0xF;
        int srIdx   = (int)(hdr >> 10) & 0x3;
        int padding = (int)(hdr >>  9) & 0x1;

        var bitrates = version == 3 ? BitratesV1 : BitratesV2;
        int br = bitrates[brIdx] * 1000;

        int sr = version switch
        {
            3 => SampleRatesV1[srIdx],
            2 => SampleRatesV2[srIdx],
            0 => SampleRatesV25[srIdx],
            _ => 0,
        };

        if (br == 0 || sr == 0)
            return null;

        int multiplier = version == 3 ? 144 : 72;
        return (multiplier * br / sr) + padding;
    }

    /// <summary>
    /// Walks the full MP3 stream and removes every Xing / Info / LAME VBR info
    /// frame. Returns the cleaned byte array.
    ///
    /// Background: each OpenAI TTS segment carries its own Xing/LAME frame.
    /// Browsers honour the frame's embedded byte-count and stop playback early.
    /// Standalone players are forgiving and skip past these frames.
    /// Removing them lets the concatenated stream play all the way through.
    /// </summary>
    public static byte[] StripVbrInfoFrames(ReadOnlySpan<byte> data, out int strippedCount)
    {
        strippedCount = 0;
        var output = new List<byte>(data.Length);
        int pos    = 0;
        int total  = data.Length;

        while (pos < total - 4)
        {
            // Sync word scan
            if (data[pos] != 0xFF || (data[pos + 1] & 0xE0) != 0xE0)
            {
                output.Add(data[pos]);
                pos++;
                continue;
            }

            uint hdr = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            int? fs  = FrameSize(data, pos);

            if (fs is null || fs < 10 || pos + fs > total)
            {
                output.Add(data[pos]);
                pos++;
                continue;
            }

            // Determine where the Xing/Info/LAME tag would sit (after 4-byte header
            // + side-information block whose length depends on version + channel mode).
            int version = (int)(hdr >> 19) & 0x3;
            int chMode  = (int)(hdr >>  6) & 0x3;

            int sideInfo = version == 3
                ? (chMode == 3 ? 17 : 32)   // MPEG1:  mono=17, stereo=32
                : (chMode == 3 ?  9 : 17);  // MPEG2/2.5: mono=9, stereo=17

            int xingPos = pos + 4 + sideInfo;

            if (xingPos + 4 <= total)
            {
                var tag = data.Slice(xingPos, 4);
                if (tag.SequenceEqual("Xing"u8) ||
                    tag.SequenceEqual("Info"u8) ||
                    tag.SequenceEqual("LAME"u8))
                {
                    strippedCount++;
                    pos += fs.Value;
                    continue;
                }
            }

            output.AddRange(data.Slice(pos, fs.Value).ToArray());
            pos += fs.Value;
        }

        // Append any remaining bytes after the last full frame
        if (pos < total)
            output.AddRange(data[pos..].ToArray());

        return [.. output];
    }
}
