namespace PodSlacker.Core.Models;

/// <summary>A single speaker turn in the podcast script.</summary>
public sealed record DialogueSegment(string Speaker, string Text);

/// <summary>A transcript entry with its video timestamp.</summary>
public sealed record TimedTranscriptEntry(double StartSeconds, string Text);

/// <summary>A captured video frame paired with the timestamp it was taken at.</summary>
public sealed record CapturedFrame(string FilePath, double TimestampSeconds);

/// <summary>Progress event emitted by the pipeline; consumed by CLI or API SSE stream.</summary>
public sealed record PipelineProgress(
    PipelineStep Step,
    string        Message,
    int           PercentComplete = 0
);

/// <summary>Identifies the current step of a pipeline run for progress reporting.</summary>
public enum PipelineStep
{
    /// <summary>Fetching the video title from YouTube.</summary>
    FetchingTitle,
    /// <summary>Downloading and parsing the video transcript.</summary>
    FetchingTranscript,
    /// <summary>Sending the transcript to the LLM to produce a markdown summary.</summary>
    GeneratingSummary,
    /// <summary>Sending the summary to the LLM to generate the podcast dialogue script.</summary>
    GeneratingScript,
    /// <summary>Synthesising speech for each script segment via the TTS API.</summary>
    GeneratingAudio,
    /// <summary>Identifying key moments and capturing screenshot frames from the video.</summary>
    CapturingFrames,
    /// <summary>Building the self-contained HTML page that bundles audio, frames, and summary.</summary>
    GeneratingPage,
    /// <summary>Uploading the HTML page to GitHub Pages.</summary>
    Publishing,
    /// <summary>The pipeline completed successfully.</summary>
    Done,
    /// <summary>The pipeline encountered an unrecoverable error and stopped.</summary>
    Error,
}

/// <summary>The full result produced by a completed pipeline run.</summary>
public sealed class PipelineResult
{
    /// <summary>The 11-character YouTube video ID (e.g. <c>dQw4w9WgXcQ</c>).</summary>
    public required string VideoId     { get; init; }
    /// <summary>The human-readable video title, or the video ID if no title was found.</summary>
    public required string Title       { get; init; }
    /// <summary>Absolute path to the generated markdown summary file.</summary>
    public required string MarkdownPath { get; init; }
    /// <summary>Absolute path to the generated MP3 audio file, or <see langword="null"/> if audio was skipped.</summary>
    public string? AudioPath           { get; init; }
    /// <summary>Absolute path to the generated HTML page, or <see langword="null"/> if page generation was skipped.</summary>
    public string? HtmlPagePath        { get; init; }
    /// <summary>The public GitHub Pages URL the page was published to, or <see langword="null"/> if publishing was skipped.</summary>
    public string? GitHubPagesUrl      { get; init; }
    /// <summary>Captured key-moment frames, each paired with its timestamp in the video.</summary>
    public IReadOnlyList<CapturedFrame> Frames { get; init; } = [];
}
