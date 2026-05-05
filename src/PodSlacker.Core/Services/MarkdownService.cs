using System.Text;
using System.Text.RegularExpressions;
using PodSlacker.Core.Models;

namespace PodSlacker.Core.Services;

/// <summary>
/// Assembles and parses the summary markdown file.
/// Mirrors the Python build_markdown() and parse_dialogue_from_markdown() functions.
/// </summary>
public static class MarkdownService
{
    /// <summary>
    /// Builds the full markdown document from summary text and dialogue segments.
    /// </summary>
    public static string BuildMarkdown(
        string                url,
        string                videoId,
        string                summary,
        List<DialogueSegment> segments,
        string?               title = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title is not null ? $"# {title}" : "# YouTube Video Summary");
        sb.AppendLine();
        sb.AppendLine($"**Source:** {url}  ");
        sb.AppendLine($"**Video ID:** `{videoId}`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(summary);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Podcast Script");
        sb.AppendLine();

        foreach (var (speaker, text) in segments)
        {
            sb.AppendLine($"**{speaker}:** {text}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads an existing summary markdown and extracts the summary text and
    /// dialogue segments. Mirrors the Python parse_dialogue_from_markdown() function —
    /// speaker names are detected dynamically from **NAME:** patterns.
    /// </summary>
    public static (string Summary, List<DialogueSegment> Segments) ParseMarkdown(string markdownPath)
    {
        string text = File.ReadAllText(markdownPath, Encoding.UTF8);

        string summary;
        string scriptPart;

        const string scriptHeader = "## Podcast Script";
        int splitIdx = text.IndexOf(scriptHeader, StringComparison.Ordinal);
        if (splitIdx >= 0)
        {
            string summaryRaw = text[..splitIdx].Trim();
            // Strip the heading and metadata lines (same as Python).
            var cleaned = summaryRaw
                .Split('\n')
                .Where(l => !l.StartsWith("# YouTube Video Summary") &&
                            !l.StartsWith("**Source:**") &&
                            !l.StartsWith("**Video ID:**") &&
                            l.Trim() != "---")
                .ToList();
            summary    = string.Join('\n', cleaned).Trim();
            scriptPart = text[(splitIdx + scriptHeader.Length)..];
        }
        else
        {
            summary    = text.Trim();
            scriptPart = string.Empty;
        }

        // Match any **NAME:** pattern — same dynamic detection as Python.
        var speakerRe = new Regex(@"^\*\*([^*]+):\*\*\s*(.*)", RegexOptions.Compiled);
        var segments  = new List<DialogueSegment>();

        foreach (string line in scriptPart.Split('\n'))
        {
            var m = speakerRe.Match(line.Trim());
            if (m.Success)
                segments.Add(new DialogueSegment(m.Groups[1].Value, m.Groups[2].Value.Trim()));
        }

        return (summary, segments);
    }
}
