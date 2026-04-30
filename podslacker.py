#!/usr/bin/env python3
"""
YouTube → Podcast Script
========================
Fetches a YouTube video's transcript, summarizes it to a markdown file,
and generates a two-host podcast MP3 using OpenAI-compatible TTS.

The LLM used for summarisation and dialogue is fully pluggable: any provider
that exposes an OpenAI-compatible chat-completions endpoint works (OpenAI,
OpenRouter, Ollama, Together AI, Groq, etc.).  TTS always uses OpenAI's
speech endpoint (most alternative providers don't offer TTS).

Usage:
    python podslacker.py <youtube_url> [options]

LLM provider options:
    --llm-base-url URL      Base URL of the chat-completions API.
                            Default: OpenAI  (https://api.openai.com/v1)
                            OpenRouter:      https://openrouter.ai/api/v1
                            Ollama (local):  http://localhost:11434/v1
    --llm-model MODEL       Model name to request.  Default: gpt-4o
                            Examples: anthropic/claude-3-5-sonnet (OpenRouter),
                                      llama3.2 (Ollama), mistral-large (Mistral)
    --llm-api-key-env VAR   Name of the environment variable that holds the
                            LLM API key.  Default: OPENAI_API_KEY
                            Example: --llm-api-key-env OPENROUTER_API_KEY

TTS provider options:
    --tts-model MODEL       TTS model to use.  Default: tts-1
    --tts-api-key-env VAR   Env var for the TTS API key.  Default: OPENAI_API_KEY
                            Set this if your TTS key differs from your LLM key.

Other options:
    --output-dir, -o        Directory to save outputs (default: current directory)
    --no-audio              Skip audio generation; only produce the markdown summary
    --reuse-summary         If a summary markdown already exists, skip the LLM
                            call and parse the dialogue from that file
    --voice-alex            OpenAI TTS voice for host Alex (default: onyx)
    --voice-jordan          OpenAI TTS voice for host Jordan (default: nova)

Requirements:
    pip install youtube-transcript-api openai
    (No ffmpeg required — audio segments are concatenated directly.)

Quick-start examples:
    # OpenAI (default)
    export OPENAI_API_KEY=sk-...
    python podslacker.py https://youtu.be/VIDEO_ID

    # OpenRouter with Claude
    export OPENROUTER_API_KEY=sk-or-...
    python podslacker.py https://youtu.be/VIDEO_ID \\
        --llm-base-url https://openrouter.ai/api/v1 \\
        --llm-model anthropic/claude-3-5-sonnet \\
        --llm-api-key-env OPENROUTER_API_KEY

    # Local Ollama (no key needed for LLM; still needs OPENAI_API_KEY for TTS)
    python podslacker.py https://youtu.be/VIDEO_ID \\
        --llm-base-url http://localhost:11434/v1 \\
        --llm-model llama3.2 \\
        --llm-api-key-env OPENAI_API_KEY
"""

import argparse
import os
import re
import sys
import tempfile
from pathlib import Path


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def get_video_id(url: str) -> str:
    """Extract the 11-character video ID from various YouTube URL formats."""
    patterns = [
        r"(?:v=|/v/|youtu\.be/|/embed/|/shorts/)([A-Za-z0-9_-]{11})",
    ]
    for pattern in patterns:
        match = re.search(pattern, url)
        if match:
            return match.group(1)
    raise ValueError(f"Could not extract a video ID from URL: {url!r}")


def fetch_transcript(
    video_id: str,
    cookies_file: str | None = None,
    proxy_url: str | None = None,
) -> str:
    """Download and concatenate all transcript segments for a video.

    Compatible with youtube-transcript-api >= 1.0 (uses the instance-based API).
    Falls back to any available language if English is not found.

    cookies_file: path to a Netscape-format cookies.txt exported from your browser.
                  Helps bypass YouTube IP blocks by authenticating as a real user.
    proxy_url:    proxy URL, e.g. "http://user:pass@host:port" or "socks5://host:port".
                  Useful when your IP is blocked by YouTube.
    """
    import http.cookiejar
    import requests
    from youtube_transcript_api import YouTubeTranscriptApi
    from youtube_transcript_api._errors import TranscriptsDisabled, NoTranscriptFound
    from youtube_transcript_api.proxies import GenericProxyConfig

    kwargs: dict = {}

    if cookies_file:
        jar = http.cookiejar.MozillaCookieJar(cookies_file)
        try:
            jar.load(ignore_discard=True, ignore_expires=True)
        except Exception as exc:
            raise RuntimeError(f"Could not load cookies file '{cookies_file}': {exc}")
        session = requests.Session()
        session.cookies = jar  # type: ignore[assignment]
        kwargs["http_client"] = session

    if proxy_url:
        kwargs["proxy_config"] = GenericProxyConfig(
            http_url=proxy_url, https_url=proxy_url
        )

    api = YouTubeTranscriptApi(**kwargs)
    try:
        # Try English first; fall back to any available language
        try:
            fetched = api.fetch(video_id, languages=["en"])
        except NoTranscriptFound:
            transcript_list = api.list(video_id)
            fetched = transcript_list.find_transcript(
                [t.language_code for t in transcript_list]
            ).fetch()

        return " ".join(snippet.text for snippet in fetched.snippets)

    except TranscriptsDisabled:
        raise RuntimeError("Transcripts are disabled for this video.")
    except NoTranscriptFound:
        raise RuntimeError(
            "No transcript found. The video may not have captions available."
        )


# ---------------------------------------------------------------------------
# AI content generation
# ---------------------------------------------------------------------------

# Hardcoded fallback prompts — used when no prompt file is found on disk.
_DEFAULT_SUMMARY_PROMPT = (
    "You are an expert content summarizer. "
    "Create a clear, well-structured markdown document summarizing a YouTube video transcript. "
    "Include: a brief overview, major sections/topics covered, key takeaways, and any notable quotes."
)

_DEFAULT_DIALOGUE_PROMPT = """\
You are a podcast script writer. Write an engaging two-host podcast episode based on a YouTube transcript.

Hosts:
- ALEX: analytical and detail-oriented — digs into the "how" and "why".
- JORDAN: curious and big-picture — focuses on real-world implications and audience questions.

Output format — one speaker turn per line, EXACTLY like this (no blank lines between turns):
[ALEX]: <what Alex says>
[JORDAN]: <what Jordan says>

Guidelines:
- Open with both hosts greeting the audience and introducing the topic.
- Naturally discuss the main ideas; don't just list facts — react, ask questions, build on each other.
- Each turn should be 2-5 natural sentences (conversational, not lecture-y).
- Aim for 10-14 total exchanges (20-28 lines).
- Close with both hosts summarising the takeaways and signing off.
"""

_DEFAULT_MONOLOGUE_PROMPT = """\
You are a podcast script writer. Write an engaging solo-host podcast episode based on a YouTube transcript.

The host is ALEX: knowledgeable and conversational — explains ideas clearly, shares opinions, and keeps the listener hooked.

Output format — one paragraph per line, EXACTLY like this (no blank lines between paragraphs):
[HOST]: <what the host says>

Guidelines:
- Open by greeting the audience and introducing the topic.
- Walk through the main ideas in a natural, flowing narrative — not a bullet-point recap.
- Each paragraph should be 2-5 sentences (conversational, not lecture-y).
- Aim for 10-16 paragraphs total.
- Close with a summary of key takeaways and a sign-off.
"""

# Default prompt file locations, relative to this script.
_SCRIPT_DIR = Path(__file__).parent
_DEFAULT_PROMPT_FILES = {
    "summary":   _SCRIPT_DIR / "prompts" / "summary.txt",
    "dialogue":  _SCRIPT_DIR / "prompts" / "dialogue.txt",
    "monologue": _SCRIPT_DIR / "prompts" / "monologue.txt",
}
_DEFAULT_PROMPT_STRINGS = {
    "summary":   _DEFAULT_SUMMARY_PROMPT,
    "dialogue":  _DEFAULT_DIALOGUE_PROMPT,
    "monologue": _DEFAULT_MONOLOGUE_PROMPT,
}


def load_prompt(name: str, override_path: str | None = None) -> str:
    """Load a prompt string, with the following priority:

    1. The file at ``override_path`` (if provided via CLI flag).
    2. The default file in the ``prompts/`` folder next to this script.
    3. The hardcoded fallback string embedded in this file.

    This means the script always works out of the box, while users can
    customise any prompt just by editing a text file.
    """
    candidates: list[Path] = []
    if override_path:
        candidates.append(Path(override_path))
    candidates.append(_DEFAULT_PROMPT_FILES[name])

    for path in candidates:
        if path.exists():
            text = path.read_text(encoding="utf-8").strip()
            if text:
                source = f"file: {path}"
                if override_path and path == Path(override_path):
                    source = f"custom file: {path}"
                print(f"   Loaded {name} prompt from {source}")
                return text
            print(f"   Warning: {path} is empty — falling back to built-in {name} prompt.")

    return _DEFAULT_PROMPT_STRINGS[name]


def generate_content(
    client,
    transcript: str,
    url: str,
    model: str = "gpt-4o",
    hosts: int = 2,
    summary_prompt: str = _DEFAULT_SUMMARY_PROMPT,
    dialogue_prompt: str = _DEFAULT_DIALOGUE_PROMPT,
    monologue_prompt: str = _DEFAULT_MONOLOGUE_PROMPT,
) -> tuple[str, list[tuple[str, str]]]:
    """Use the provided OpenAI-compatible client to produce:
      1. A markdown summary string.
      2. A list of (speaker, text) segments.

    hosts=1 produces a solo monologue (speaker is always "HOST").
    hosts=2 produces a two-host dialogue (speakers are "ALEX" and "JORDAN").
    The three *_prompt arguments accept the system prompt strings to use for
    each LLM call, loaded externally via load_prompt().
    """
    # Truncate very long transcripts so we stay within context limits
    MAX_CHARS = 14_000
    excerpt = transcript[:MAX_CHARS]
    if len(transcript) > MAX_CHARS:
        excerpt += "\n\n[transcript truncated for length]"

    print(f"   Generating markdown summary  (model: {model})…")
    summary_resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": summary_prompt},
            {
                "role": "user",
                "content": (
                    f"Source URL: {url}\n\n"
                    f"Transcript:\n{excerpt}"
                ),
            },
        ],
    )
    markdown = summary_resp.choices[0].message.content.strip()

    script_prompt = monologue_prompt if hosts == 1 else dialogue_prompt
    host_label = "solo monologue" if hosts == 1 else "two-host dialogue"
    print(f"   Generating {host_label} script  (model: {model})…")
    script_resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": script_prompt},
            {
                "role": "user",
                "content": (
                    f"Source URL: {url}\n\n"
                    f"Transcript:\n{excerpt}"
                ),
            },
        ],
    )
    raw_script = script_resp.choices[0].message.content.strip()

    # Parse into structured segments
    segments: list[tuple[str, str]] = []
    for line in raw_script.splitlines():
        line = line.strip()
        if line.startswith("[HOST]:"):
            segments.append(("HOST", line[7:].strip()))
        elif line.startswith("[ALEX]:"):
            segments.append(("ALEX", line[7:].strip()))
        elif line.startswith("[JORDAN]:"):
            segments.append(("JORDAN", line[9:].strip()))

    if not segments:
        raise RuntimeError(
            "Could not parse any script lines from the AI response. "
            "Raw output:\n" + raw_script
        )

    return markdown, segments


# ---------------------------------------------------------------------------
# Audio generation
# ---------------------------------------------------------------------------

# A tiny valid silent MP3 frame used as a short pause between speaker turns.
# This is a single 128kbps stereo frame of silence (~26ms), repeated to fill
# roughly 0.5 s.  No ffmpeg required — we just concatenate raw MP3 bytes.
_SILENT_MP3_FRAME = bytes([
    0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
])
_PAUSE_BYTES = _SILENT_MP3_FRAME * 20  # ~0.5 s of silence


def generate_audio(
    client,
    segments: list[tuple[str, str]],
    output_path: Path,
    voice_alex: str = "onyx",
    voice_jordan: str = "nova",
    tts_model: str = "tts-1",
) -> None:
    """Generate TTS for each dialogue segment and stitch them into one MP3.

    Uses raw byte concatenation — no ffmpeg or pydub required.
    The client should be pointed at an OpenAI-compatible TTS endpoint.
    """
    voice_map = {"ALEX": voice_alex, "JORDAN": voice_jordan, "HOST": voice_alex}
    total = len(segments)
    chunks: list[bytes] = []

    for i, (speaker, text) in enumerate(segments, 1):
        print(f"   Segment {i}/{total}  [{speaker}]  \"{text[:60]}...\"")
        voice = voice_map[speaker]

        response = client.audio.speech.create(
            model=tts_model,
            voice=voice,
            input=text,
        )
        chunks.append(response.content)
        if i < total:
            chunks.append(_PAUSE_BYTES)

    output_path.write_bytes(b"".join(chunks))


def generate_audio_kokoro(
    segments: list[tuple[str, str]],
    output_path: Path,
    voice_alex: str = "am_michael",
    voice_jordan: str = "af_heart",
    lang_code: str = "a",
    speed: float = 1.0,
) -> None:
    """Generate TTS for each dialogue segment using Kokoro and write a WAV file.

    Kokoro runs entirely locally on CPU — no API key or internet access required
    after the model weights are downloaded on first run (~82 MB from HuggingFace).

    Requires:  pip install kokoro
    Output:    a mono 24 kHz WAV file (universally playable, no ffmpeg needed).

    voice_alex / voice_jordan — Kokoro voice IDs.
        American English voices:
            Female: af_heart (default for Jordan), af_bella, af_nicole, af_sarah, af_sky
            Male:   am_michael (default for Alex), am_adam
        British English voices (use lang_code='b'):
            Female: bf_emma, bf_isabella
            Male:   bm_george, bm_lewis
    lang_code — 'a' for American English, 'b' for British English.
    speed     — Speech rate multiplier (1.0 = normal, 1.2 = 20% faster, etc.).
    """
    try:
        from kokoro import KPipeline
        import numpy as np
    except ImportError:
        print(
            "\n  ✗ kokoro is not installed. Run: pip install kokoro\n"
            "    On first run, model weights (~82 MB) are downloaded automatically."
        )
        sys.exit(1)

    import wave

    SAMPLE_RATE = 24_000
    PAUSE_SAMPLES = int(SAMPLE_RATE * 0.6)  # 0.6 s of silence between turns

    voice_map = {"ALEX": voice_alex, "JORDAN": voice_jordan, "HOST": voice_alex}
    total = len(segments)

    print(f"   Loading Kokoro model (lang={lang_code}, device=cpu)...")
    pipeline = KPipeline(lang_code=lang_code, repo_id="hexgrad/Kokoro-82M")

    all_audio: list[np.ndarray] = []
    silence = np.zeros(PAUSE_SAMPLES, dtype=np.float32)

    for i, (speaker, text) in enumerate(segments, 1):
        print(f"   Segment {i}/{total}  [{speaker}]  \"{text[:60]}...\"")
        voice = voice_map[speaker]

        # Kokoro may split long text into multiple chunks; concatenate them all.
        chunks: list[np.ndarray] = []
        for result in pipeline(text, voice=voice, speed=speed):
            if result.audio is not None:
                chunks.append(result.audio.numpy())

        if not chunks:
            print(f"   Warning: no audio generated for segment {i} — skipping.")
            continue

        all_audio.append(np.concatenate(chunks))
        if i < total:
            all_audio.append(silence)

    if not all_audio:
        raise RuntimeError("Kokoro produced no audio output.")

    combined = np.concatenate(all_audio)

    # Write as 16-bit mono WAV using Python's stdlib — no ffmpeg required.
    audio_int16 = np.clip(combined * 32767, -32768, 32767).astype(np.int16)
    with wave.open(str(output_path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)       # 2 bytes = 16-bit
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(audio_int16.tobytes())


# ---------------------------------------------------------------------------
# Markdown assembly & parsing
# ---------------------------------------------------------------------------

def parse_dialogue_from_markdown(md_path: Path) -> tuple[str, list[tuple[str, str]]]:
    """Read an existing summary markdown and extract the summary + dialogue segments.

    The file is expected to have a '## Podcast Script' section where each line
    looks like:  **ALEX:** some text   or   **JORDAN:** some text
    """
    text = md_path.read_text(encoding="utf-8")

    # Split into summary part and podcast script part
    if "## Podcast Script" in text:
        summary_part, script_part = text.split("## Podcast Script", 1)
        # Strip the trailing --- and header from the summary block
        summary_part = summary_part.strip().lstrip("# YouTube Video Summary").strip()
        # Remove the metadata lines (Source / Video ID) and leading ---
        lines = summary_part.splitlines()
        cleaned = [l for l in lines if not l.startswith("**Source:**")
                   and not l.startswith("**Video ID:**")
                   and l.strip() != "---"]
        summary = "\n".join(cleaned).strip()
    else:
        summary = text.strip()
        script_part = ""

    segments: list[tuple[str, str]] = []
    for line in script_part.splitlines():
        line = line.strip()
        if line.startswith("**ALEX:**"):
            segments.append(("ALEX", line[9:].strip()))
        elif line.startswith("**JORDAN:**"):
            segments.append(("JORDAN", line[11:].strip()))

    return summary, segments


def build_markdown(url: str, video_id: str, summary: str, segments: list[tuple[str, str]]) -> str:
    lines = [
        f"# YouTube Video Summary",
        f"",
        f"**Source:** {url}  ",
        f"**Video ID:** `{video_id}`",
        f"",
        f"---",
        f"",
        summary,
        f"",
        f"---",
        f"",
        f"## Podcast Script",
        f"",
    ]
    for speaker, text in segments:
        lines.append(f"**{speaker}:** {text}")
        lines.append("")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def _require_env(var: str, label: str) -> str:
    """Read an environment variable or exit with a clear error."""
    value = os.environ.get(var)
    if not value:
        print(f"Error: {label} environment variable '{var}' is not set.")
        sys.exit(1)
    return value


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Convert a YouTube video into a markdown summary + two-host podcast MP3.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("url", help="YouTube video URL")

    # ---- YouTube access flags ----
    yt_group = parser.add_argument_group("YouTube access (use if transcript fetch is blocked)")
    yt_group.add_argument(
        "--cookies",
        metavar="FILE",
        default=None,
        help=(
            "Path to a Netscape-format cookies.txt file exported from your browser "
            "while logged into YouTube. Helps bypass IP blocks. "
            "Use a browser extension such as 'Get cookies.txt LOCALLY' to export."
        ),
    )
    yt_group.add_argument(
        "--proxy",
        metavar="URL",
        default=None,
        help=(
            "Proxy URL for transcript requests, e.g. http://user:pass@host:port "
            "or socks5://host:port. Useful when your IP is blocked by YouTube."
        ),
    )

    parser.add_argument(
        "--output-dir", "-o",
        default="outputs",
        metavar="DIR",
        help="Directory for output files (default: outputs/)",
    )
    parser.add_argument(
        "--no-audio",
        action="store_true",
        help="Skip audio generation — only produce the markdown summary",
    )
    parser.add_argument(
        "--hosts",
        type=int,
        default=2,
        choices=[1, 2],
        help="Number of podcast hosts: 1 for a solo monologue, 2 for a two-host dialogue (default: 2)",
    )
    parser.add_argument(
        "--reuse-summary",
        action="store_true",
        help=(
            "If a summary markdown for this video already exists in --output-dir, "
            "skip the LLM call and read the dialogue from that file. "
            "Useful for regenerating audio without re-incurring LLM costs."
        ),
    )

    # ---- Prompt customisation flags ----
    prompt_group = parser.add_argument_group(
        "Prompt customisation",
        "Override the system prompts sent to the LLM. Each flag accepts a path to a plain "
        "text file. If omitted, podslacker looks for the file in the prompts/ folder next "
        "to the script, then falls back to the built-in defaults.",
    )
    prompt_group.add_argument(
        "--summary-prompt",
        metavar="FILE",
        default=None,
        help="System prompt for the markdown summary step (default: prompts/summary.txt)",
    )
    prompt_group.add_argument(
        "--dialogue-prompt",
        metavar="FILE",
        default=None,
        help="System prompt for the two-host dialogue step (default: prompts/dialogue.txt)",
    )
    prompt_group.add_argument(
        "--monologue-prompt",
        metavar="FILE",
        default=None,
        help="System prompt for the solo monologue step (default: prompts/monologue.txt)",
    )

    # ---- LLM provider flags ----
    llm_group = parser.add_argument_group("LLM provider (summarisation & dialogue)")
    llm_group.add_argument(
        "--llm-base-url",
        default=None,
        metavar="URL",
        help=(
            "Base URL of an OpenAI-compatible chat-completions API. "
            "Defaults to OpenAI. "
            "Examples: https://openrouter.ai/api/v1  |  http://localhost:11434/v1"
        ),
    )
    llm_group.add_argument(
        "--llm-model",
        default="gpt-4o",
        metavar="MODEL",
        help=(
            "Model name to request from the LLM provider. Default: gpt-4o. "
            "Examples: anthropic/claude-3-5-sonnet (OpenRouter), llama3.2 (Ollama)"
        ),
    )
    llm_group.add_argument(
        "--llm-api-key-env",
        default="OPENAI_API_KEY",
        metavar="VAR",
        help=(
            "Name of the environment variable that holds the LLM API key. "
            "Default: OPENAI_API_KEY. "
            "Example: --llm-api-key-env OPENROUTER_API_KEY"
        ),
    )

    # ---- TTS engine selection ----
    parser.add_argument(
        "--tts-engine",
        default="openai",
        choices=["openai", "kokoro"],
        help=(
            "TTS engine to use for audio generation. "
            "'openai' (default) uses the OpenAI speech API — requires OPENAI_API_KEY. "
            "'kokoro' runs a local open-source model on CPU — free, no API key needed, "
            "outputs a .wav file. Requires: pip install kokoro"
        ),
    )

    # ---- OpenAI TTS flags ----
    tts_group = parser.add_argument_group(
        "OpenAI TTS options",
        "Used when --tts-engine openai (the default).",
    )
    tts_group.add_argument(
        "--tts-model",
        default="tts-1",
        metavar="MODEL",
        help="OpenAI TTS model. Default: tts-1  (tts-1-hd for higher quality)",
    )
    tts_group.add_argument(
        "--tts-api-key-env",
        default="OPENAI_API_KEY",
        metavar="VAR",
        help=(
            "Name of the environment variable that holds the TTS API key. "
            "Default: OPENAI_API_KEY. Useful when your TTS key differs from your LLM key."
        ),
    )
    tts_group.add_argument(
        "--voice-alex",
        default="onyx",
        choices=["alloy", "echo", "fable", "onyx", "nova", "shimmer"],
        help="OpenAI voice for host Alex (default: onyx)",
    )
    tts_group.add_argument(
        "--voice-jordan",
        default="nova",
        choices=["alloy", "echo", "fable", "onyx", "nova", "shimmer"],
        help="OpenAI voice for host Jordan (default: nova)",
    )

    # ---- Kokoro TTS flags ----
    kokoro_group = parser.add_argument_group(
        "Kokoro TTS options",
        "Used when --tts-engine kokoro. Runs locally on CPU; no API key required. "
        "Model weights (~82 MB) are downloaded automatically from HuggingFace on first run.",
    )
    kokoro_group.add_argument(
        "--kokoro-voice-alex",
        default="am_michael",
        metavar="VOICE",
        help=(
            "Kokoro voice ID for host Alex. Default: am_michael (American male). "
            "American male: am_michael, am_adam. "
            "American female: af_heart, af_bella, af_nicole, af_sarah, af_sky. "
            "British (use --kokoro-lang b): bm_george, bm_lewis, bf_emma, bf_isabella."
        ),
    )
    kokoro_group.add_argument(
        "--kokoro-voice-jordan",
        default="af_heart",
        metavar="VOICE",
        help=(
            "Kokoro voice ID for host Jordan. Default: af_heart (American female). "
            "In --hosts 1 mode this flag is unused; Alex's voice is used throughout."
        ),
    )
    kokoro_group.add_argument(
        "--kokoro-lang",
        default="a",
        choices=["a", "b"],
        help="Kokoro language variant: 'a' = American English (default), 'b' = British English.",
    )
    kokoro_group.add_argument(
        "--kokoro-speed",
        type=float,
        default=1.0,
        metavar="RATE",
        help="Speech rate multiplier for Kokoro. Default: 1.0 (normal speed). Try 1.1 for slightly faster delivery.",
    )

    args = parser.parse_args()

    from openai import OpenAI

    # ---- Build LLM client ----
    llm_api_key = _require_env(args.llm_api_key_env, "LLM API key")
    llm_kwargs: dict = {"api_key": llm_api_key}
    if args.llm_base_url:
        llm_kwargs["base_url"] = args.llm_base_url
    llm_client = OpenAI(**llm_kwargs)

    # ---- Build OpenAI TTS client (only needed for --tts-engine openai) ----
    tts_client = None
    if not args.no_audio and args.tts_engine == "openai":
        if args.tts_api_key_env == args.llm_api_key_env and args.llm_base_url is None:
            tts_client = llm_client  # exact same config — reuse
        else:
            tts_api_key = _require_env(args.tts_api_key_env, "TTS API key")
            tts_client = OpenAI(api_key=tts_api_key)

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    # ---- Step 1: Transcript ----
    print(f"\n🎬  Processing: {args.url}")
    print("\n📝  Fetching transcript…")
    try:
        video_id = get_video_id(args.url)
        transcript = fetch_transcript(video_id, cookies_file=args.cookies, proxy_url=args.proxy)
        print(f"   ✓ Got transcript ({len(transcript):,} characters)")
    except Exception as exc:
        print(f"   ✗ {exc}")
        sys.exit(1)

    transcript_path = output_dir / f"{video_id}_transcript.md"
    transcript_md = (
        f"# Transcript\n\n"
        f"**Source:** {args.url}  \n"
        f"**Video ID:** `{video_id}`\n\n"
        f"---\n\n"
        f"{transcript}\n"
    )
    transcript_path.write_text(transcript_md, encoding="utf-8")
    print(f"   ✓ Transcript saved → {transcript_path}")

    # ---- Step 2: AI content (or load from cache) ----
    md_path = output_dir / f"{video_id}_summary.md"

    if args.reuse_summary and md_path.exists():
        print(f"\n📋  Reusing existing summary: {md_path}")
        try:
            markdown_summary, dialogue_segments = parse_dialogue_from_markdown(md_path)
            print(f"   ✓ Summary: {len(markdown_summary):,} characters")
            print(f"   ✓ Dialogue: {len(dialogue_segments)} segments")
            if not dialogue_segments:
                print("   ✗ No script segments found in the existing file.")
                print("     Remove --reuse-summary to regenerate.")
                sys.exit(1)
        except Exception as exc:
            print(f"   ✗ Failed to parse existing markdown: {exc}")
            sys.exit(1)
    else:
        if args.reuse_summary:
            print(f"\n⚠️   --reuse-summary set but no existing file found at {md_path}")
            print("     Falling back to LLM generation.")
        llm_label = args.llm_base_url or "OpenAI"
        host_label = "solo" if args.hosts == 1 else "two-host"
        print(f"\n🤖  Generating {host_label} podcast script  [{llm_label} / {args.llm_model}]…")
        summary_prompt   = load_prompt("summary",   args.summary_prompt)
        dialogue_prompt  = load_prompt("dialogue",  args.dialogue_prompt)
        monologue_prompt = load_prompt("monologue", args.monologue_prompt)
        try:
            markdown_summary, dialogue_segments = generate_content(
                llm_client, transcript, args.url,
                model=args.llm_model,
                hosts=args.hosts,
                summary_prompt=summary_prompt,
                dialogue_prompt=dialogue_prompt,
                monologue_prompt=monologue_prompt,
            )
            print(f"   ✓ Summary: {len(markdown_summary):,} characters")
            print(f"   ✓ Dialogue: {len(dialogue_segments)} segments")
        except Exception as exc:
            print(f"   ✗ {exc}")
            sys.exit(1)

        # ---- Step 3: Save markdown ----
        md_content = build_markdown(args.url, video_id, markdown_summary, dialogue_segments)
        md_path.write_text(md_content, encoding="utf-8")
        print(f"\n📄  Markdown saved → {md_path}")

    # ---- Step 4: Audio ----
    if not args.no_audio:
        if args.tts_engine == "kokoro":
            audio_path = output_dir / f"{video_id}_podcast.wav"
            print(f"\n🎙️   Generating podcast audio  [Kokoro / local CPU]…")
            print(f"   Voices: Alex={args.kokoro_voice_alex}, Jordan={args.kokoro_voice_jordan}")
            try:
                generate_audio_kokoro(
                    dialogue_segments,
                    audio_path,
                    voice_alex=args.kokoro_voice_alex,
                    voice_jordan=args.kokoro_voice_jordan,
                    lang_code=args.kokoro_lang,
                    speed=args.kokoro_speed,
                )
                print(f"\n🎧  Podcast saved → {audio_path}  (WAV, 24 kHz mono)")
            except Exception as exc:
                print(f"   ✗ Audio generation failed: {exc}")
                sys.exit(1)
        else:
            audio_path = output_dir / f"{video_id}_podcast.mp3"
            print(f"\n🎙️   Generating podcast audio  [OpenAI TTS / {args.tts_model}]…")
            try:
                generate_audio(
                    tts_client,
                    dialogue_segments,
                    audio_path,
                    voice_alex=args.voice_alex,
                    voice_jordan=args.voice_jordan,
                    tts_model=args.tts_model,
                )
                print(f"\n🎧  Podcast saved → {audio_path}")
            except Exception as exc:
                print(f"   ✗ Audio generation failed: {exc}")
                sys.exit(1)

    print("\n✅  All done!\n")


if __name__ == "__main__":
    main()
