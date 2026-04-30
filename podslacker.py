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
import json
import os
import re
import sys
import tempfile
from pathlib import Path


# ---------------------------------------------------------------------------
# Config file loading
# ---------------------------------------------------------------------------

_DEFAULT_CONFIG_PATH = _SCRIPT_DIR_EARLY = Path(__file__).parent / "podslacker.json"


def load_config(path: Path) -> dict:
    """Load a JSON config file and return a dict of non-null values.

    Keys that are null/None in the file are skipped so argparse hardcoded
    defaults are used for those fields instead.  An unknown key triggers a
    warning rather than an error so old config files keep working after
    the script is updated.
    """
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return {}
    except json.JSONDecodeError as exc:
        print(f"⚠️   Could not parse config file {path}: {exc}")
        return {}

    valid_keys = {
        "output_dir", "hosts", "host1_name", "host2_name", "no_audio", "reuse_summary", "num_frames",
        "cookies", "proxy",
        "summary_prompt", "dialogue_prompt", "monologue_prompt", "key_moments_prompt",
        "llm_base_url", "llm_model", "llm_api_key_env",
        "summary_model", "summary_base_url", "summary_api_key_env",
        "script_model", "script_base_url", "script_api_key_env",
        "key_moments_model", "key_moments_base_url", "key_moments_api_key_env",
        "tts_engine", "tts_model", "tts_api_key_env", "voice_host1", "voice_host2",
        "kokoro_voice_host1", "kokoro_voice_host2", "kokoro_lang", "kokoro_speed",
        "no_page", "publish_github", "github_repo", "github_token_env", "github_branch",
    }

    result = {}
    for key, value in raw.items():
        if key.startswith("_"):
            continue  # ignore comment keys like "_comment"
        if key not in valid_keys:
            print(f"⚠️   Unknown key in config file: '{key}' — ignoring.")
            continue
        if value is not None:  # null entries are skipped; argparse default takes over
            result[key] = value

    return result


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


def fetch_video_title(url: str) -> str | None:
    """Fetch the video title, trying two methods in order:

    1. YouTube oEmbed API — fast, no extra dependencies, no API key needed.
    2. yt-dlp metadata extraction — used as a fallback if oEmbed fails.

    Returns the title string, or None if both methods fail.  Warnings are
    printed so failures are visible rather than silently swallowed.
    """
    import requests

    # --- Method 1: oEmbed (fast, no extra dependencies) ---
    try:
        resp = requests.get(
            "https://www.youtube.com/oembed",
            params={"url": url, "format": "json"},
            timeout=10,
        )
        resp.raise_for_status()
        title = resp.json().get("title")
        if title:
            return title
    except Exception as exc:
        print(f"   ⚠️   oEmbed title fetch failed ({type(exc).__name__}: {exc}) — trying yt-dlp…")

    # --- Method 2: yt-dlp fallback ---
    try:
        import yt_dlp  # type: ignore
        ydl_opts = {"quiet": True, "no_warnings": True, "skip_download": True}
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info = ydl.extract_info(url, download=False)
            title = (info or {}).get("title")
            if title:
                return title
    except Exception as exc:
        print(f"   ⚠️   yt-dlp title fetch failed ({type(exc).__name__}: {exc})")

    return None


def sanitize_title(title: str, max_len: int = 50) -> str:
    """Convert a video title to a filesystem-safe slug.

    Lowercases the title, strips characters that are not alphanumeric,
    spaces, or hyphens, collapses runs of whitespace/hyphens to a single
    underscore, trims leading/trailing underscores, and truncates to
    max_len characters.

    Examples:
        "My Great Video! (2024)"  →  "my_great_video_2024"
        "AI & ML: What's Next?"   →  "ai_ml_whats_next"
    """
    slug = title.lower()
    slug = re.sub(r"[^\w\s-]", "", slug)        # keep alphanumeric, spaces, hyphens
    slug = re.sub(r"[\s\-]+", "_", slug)         # collapse whitespace/hyphens → _
    slug = slug.strip("_")[:max_len].rstrip("_") # trim and truncate
    return slug or "untitled"


def fetch_transcript(
    video_id: str,
    cookies_file: str | None = None,
    proxy_url: str | None = None,
) -> tuple[str, list[tuple[float, str]]]:
    """Download and concatenate all transcript segments for a video.

    Returns a tuple of:
      - Full concatenated transcript text (str)
      - List of (start_seconds, text) timed entries for frame capture

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

        snippets = fetched.snippets
        full_text = " ".join(s.text for s in snippets)
        timed_entries = [(s.start, s.text) for s in snippets]
        return full_text, timed_entries

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
- {host1_name}: analytical and detail-oriented — digs into the "how" and "why".
- {host2_name}: curious and big-picture — focuses on real-world implications and audience questions.

Output format — one speaker turn per line, EXACTLY like this (no blank lines between turns):
[{host1_name}]: <what {host1_name} says>
[{host2_name}]: <what {host2_name} says>

Guidelines:
- Open with both hosts greeting the audience and introducing the topic.
- Naturally discuss the main ideas; don't just list facts — react, ask questions, build on each other.
- Each turn should be 2-5 natural sentences (conversational, not lecture-y).
- Aim for 10-14 total exchanges (20-28 lines).
- Close with both hosts summarising the takeaways and signing off.
"""

_DEFAULT_MONOLOGUE_PROMPT = """\
You are a podcast script writer. Write an engaging solo-host podcast episode based on a YouTube transcript.

The host is {host1_name}: knowledgeable and conversational — explains ideas clearly, shares opinions, and keeps the listener hooked.

Output format — one paragraph per line, EXACTLY like this (no blank lines between paragraphs):
[{host1_name}]: <what {host1_name} says>

Guidelines:
- Open by greeting the audience and introducing the topic.
- Walk through the main ideas in a natural, flowing narrative — not a bullet-point recap.
- Each paragraph should be 2-5 sentences (conversational, not lecture-y).
- Aim for 10-16 paragraphs total.
- Close with a summary of key takeaways and a sign-off.
"""

# The {num_frames} placeholder is substituted at runtime with the actual count.
_DEFAULT_KEY_MOMENTS_PROMPT = """\
You are a video analyst. Given a timestamped transcript excerpt, identify the most \
visually interesting or important moments in the video. \
These should be spread across the video — avoid clustering all picks near the start.

Return ONLY a JSON array of exactly {num_frames} numbers representing timestamps \
in seconds (floats). Example: [12.5, 45.0, 120.3, 240.0, 310.5]
No explanation, no markdown, just the JSON array.\
"""

# Default prompt file locations, relative to this script.
_SCRIPT_DIR = Path(__file__).parent
_DEFAULT_PROMPT_FILES = {
    "summary":     _SCRIPT_DIR / "prompts" / "summary.txt",
    "dialogue":    _SCRIPT_DIR / "prompts" / "dialogue.txt",
    "monologue":   _SCRIPT_DIR / "prompts" / "monologue.txt",
    "key_moments": _SCRIPT_DIR / "prompts" / "key_moments.txt",
}
_DEFAULT_PROMPT_STRINGS = {
    "summary":     _DEFAULT_SUMMARY_PROMPT,
    "dialogue":    _DEFAULT_DIALOGUE_PROMPT,
    "monologue":   _DEFAULT_MONOLOGUE_PROMPT,
    "key_moments": _DEFAULT_KEY_MOMENTS_PROMPT,
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


def _truncate_transcript(transcript: str, max_chars: int = 14_000) -> str:
    """Truncate a transcript to max_chars, appending a notice if cut."""
    if len(transcript) <= max_chars:
        return transcript
    return transcript[:max_chars] + "\n\n[transcript truncated for length]"


def generate_summary(
    client,
    transcript: str,
    url: str,
    model: str = "gpt-4o",
    summary_prompt: str = _DEFAULT_SUMMARY_PROMPT,
) -> str:
    """Call the LLM to produce a markdown summary of the transcript.

    Returns the raw markdown string.
    """
    excerpt = _truncate_transcript(transcript)
    print(f"   Generating markdown summary  (model: {model})…")
    resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": summary_prompt},
            {
                "role": "user",
                "content": f"Source URL: {url}\n\nTranscript:\n{excerpt}",
            },
        ],
    )
    return resp.choices[0].message.content.strip()


def generate_script(
    client,
    transcript: str,
    url: str,
    model: str = "gpt-4o",
    hosts: int = 2,
    host1_name: str = "ALEX",
    host2_name: str = "JORDAN",
    dialogue_prompt: str = _DEFAULT_DIALOGUE_PROMPT,
    monologue_prompt: str = _DEFAULT_MONOLOGUE_PROMPT,
) -> list[tuple[str, str]]:
    """Call the LLM to produce a podcast script and parse it into segments.

    hosts=1 produces a solo monologue; hosts=2 a two-host dialogue.
    host1_name / host2_name are substituted into the prompt and used as the
    speaker tags when parsing the LLM's output.

    Returns a list of (speaker, text) tuples where speaker is host1_name,
    host2_name, or host1_name (monologue).
    """
    excerpt = _truncate_transcript(transcript)
    raw_prompt = monologue_prompt if hosts == 1 else dialogue_prompt
    try:
        script_prompt = raw_prompt.format(host1_name=host1_name, host2_name=host2_name)
    except KeyError as exc:
        raise RuntimeError(
            f"Script prompt contains an unknown placeholder: {exc}. "
            "Supported placeholders: {{host1_name}}, {{host2_name}}"
        )

    host_label = "solo monologue" if hosts == 1 else "two-host dialogue"
    print(f"   Generating {host_label} script  (model: {model})…")
    resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": script_prompt},
            {
                "role": "user",
                "content": f"Source URL: {url}\n\nTranscript:\n{excerpt}",
            },
        ],
    )
    raw_script = resp.choices[0].message.content.strip()

    # Build speaker tags dynamically from the configured names.
    tags = [host1_name]
    if hosts == 2:
        tags.append(host2_name)

    segments: list[tuple[str, str]] = []
    for line in raw_script.splitlines():
        line = line.strip()
        for tag in tags:
            prefix = f"[{tag}]:"
            if line.startswith(prefix):
                segments.append((tag, line[len(prefix):].strip()))
                break

    if not segments:
        raise RuntimeError(
            "Could not parse any script lines from the AI response. "
            "Raw output:\n" + raw_script
        )
    return segments


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
    voice_host1: str = "onyx",
    voice_host2: str = "nova",
    tts_model: str = "tts-1",
    host1_name: str = "ALEX",
    host2_name: str = "JORDAN",
) -> None:
    """Generate TTS for each dialogue segment and stitch them into one MP3.

    Uses raw byte concatenation — no ffmpeg or pydub required.
    The client should be pointed at an OpenAI-compatible TTS endpoint.
    """
    voice_map = {host1_name: voice_host1, host2_name: voice_host2}
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
    voice_host1: str = "am_michael",
    voice_host2: str = "af_heart",
    lang_code: str = "a",
    speed: float = 1.0,
    host1_name: str = "ALEX",
    host2_name: str = "JORDAN",
) -> None:
    """Generate TTS for each dialogue segment using Kokoro and write a WAV file.

    Kokoro runs entirely locally on CPU — no API key or internet access required
    after the model weights are downloaded on first run (~82 MB from HuggingFace).

    Requires:  pip install kokoro
    Output:    a mono 24 kHz WAV file (universally playable, no ffmpeg needed).

    voice_host1 / voice_host2 — Kokoro voice IDs.
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

    voice_map = {host1_name: voice_host1, host2_name: voice_host2}
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
    looks like:  **SPEAKERNAME:** some text

    Speaker names are detected dynamically — any **NAME:** pattern is accepted,
    so the file works regardless of what host names were used when it was generated.
    """
    text = md_path.read_text(encoding="utf-8")

    # Split into summary part and podcast script part
    if "## Podcast Script" in text:
        summary_part, script_part = text.split("## Podcast Script", 1)
        summary_part = summary_part.strip().lstrip("# YouTube Video Summary").strip()
        lines = summary_part.splitlines()
        cleaned = [l for l in lines if not l.startswith("**Source:**")
                   and not l.startswith("**Video ID:**")
                   and l.strip() != "---"]
        summary = "\n".join(cleaned).strip()
    else:
        summary = text.strip()
        script_part = ""

    # Match any **NAME:** pattern rather than hardcoded ALEX / JORDAN.
    speaker_pattern = re.compile(r"^\*\*([^*]+):\*\*\s*(.*)")
    segments: list[tuple[str, str]] = []
    for line in script_part.splitlines():
        m = speaker_pattern.match(line.strip())
        if m:
            segments.append((m.group(1), m.group(2).strip()))

    return summary, segments


def build_markdown(
    url: str,
    video_id: str,
    summary: str,
    segments: list[tuple[str, str]],
    title: str | None = None,
) -> str:
    heading = f"# {title}" if title else "# YouTube Video Summary"
    lines = [
        heading,
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
# Frame capture
# ---------------------------------------------------------------------------

def identify_key_moments(
    client,
    timed_entries: list[tuple[float, str]],
    num_frames: int,
    model: str,
    url: str,
    system_prompt: str = _DEFAULT_KEY_MOMENTS_PROMPT,
) -> list[float]:
    """Ask the LLM to identify the N most visually interesting / key moments.

    Sends the LLM a condensed timestamped transcript and asks it to return
    exactly ``num_frames`` timestamps (in seconds) that correspond to major
    topic changes, key reveals, or visually significant moments.

    system_prompt may contain a ``{num_frames}`` placeholder which is
    substituted with the actual count before sending to the LLM.

    Returns a sorted list of timestamps as floats (seconds from start).
    """
    import json

    # Build a compact timestamped transcript for the prompt.
    # Include one entry roughly every 10 seconds to keep token count low.
    sampled: list[str] = []
    last_included = -999.0
    for start, text in timed_entries:
        if start - last_included >= 10.0:
            mins, secs = divmod(int(start), 60)
            sampled.append(f"[{mins:02d}:{secs:02d}] {text.strip()}")
            last_included = start

    transcript_excerpt = "\n".join(sampled)
    total_duration = timed_entries[-1][0] if timed_entries else 0

    # Substitute placeholders so the prompt file can reference frame counts.
    try:
        system = system_prompt.format(
            num_frames=num_frames,
            num_frames_minus_one=max(num_frames - 1, 0),
        )
    except KeyError as exc:
        raise RuntimeError(
            f"Key moments prompt contains an unknown placeholder: {exc}. "
            "Supported placeholders: {{num_frames}}, {{num_frames_minus_one}}"
        )

    user_msg = (
        f"Video URL: {url}\n"
        f"Total duration (approx): {int(total_duration // 60)}m {int(total_duration % 60)}s\n\n"
        f"Timestamped transcript:\n{transcript_excerpt}"
    )

    print(f"   Asking LLM to identify {num_frames} key moments  (model: {model})...")
    resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": system},
            {"role": "user", "content": user_msg},
        ],
        temperature=0.3,
    )

    raw = resp.choices[0].message.content.strip()
    # Strip markdown code fences if the LLM added them
    raw = re.sub(r"```[a-z]*\n?", "", raw).strip()

    try:
        timestamps = json.loads(raw)
        if not isinstance(timestamps, list):
            raise ValueError("Expected a JSON array")
        timestamps = [float(t) for t in timestamps]
    except Exception as exc:
        raise RuntimeError(
            f"LLM returned an unexpected format for key moments.\n"
            f"Raw response: {raw!r}\n"
            f"Parse error: {exc}"
        )

    # Clamp to valid range and sort
    max_ts = total_duration if total_duration > 0 else float("inf")
    timestamps = sorted(max(0.0, min(t, max_ts)) for t in timestamps)
    return timestamps[:num_frames]  # ensure we never return more than requested


def get_video_stream_url(video_url: str) -> str:
    """Use yt-dlp to resolve the best video stream URL without downloading.

    Returns the direct HTTP URL of the best available video stream (up to 720p
    to keep seeking fast).  Raises RuntimeError if yt-dlp can't find a stream.
    """
    try:
        import yt_dlp  # type: ignore
    except ImportError:
        raise RuntimeError(
            "yt-dlp is not installed. Run: pip install yt-dlp"
        )

    ydl_opts = {
        # Prefer H.264 (avc1) streams — OpenCV's bundled FFmpeg decodes these
        # reliably on all platforms.  AV1 (av01) and VP9 streams often fail
        # because OpenCV's FFmpeg build lacks a software AV1 decoder, and VP9
        # seeking via HTTP range requests is unreliable.
        # Priority: H.264 ≤720p → H.264 any height → VP9 ≤720p → anything ≤720p
        "format": (
            "bestvideo[height<=720][vcodec^=avc1]"
            "/bestvideo[vcodec^=avc1]"
            "/bestvideo[height<=720][vcodec^=vp9]"
            "/bestvideo[height<=720]"
            "/bestvideo"
        ),
        "quiet": True,
        "no_warnings": True,
        "skip_download": True,
    }

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        info = ydl.extract_info(video_url, download=False)

    if info is None:
        raise RuntimeError("yt-dlp returned no info for the video.")

    # The direct playback URL lives in url (for single-format) or formats[-1]
    stream_url = info.get("url")
    if not stream_url and "formats" in info:
        # Pick the last format entry, which is the selected best
        stream_url = info["formats"][-1].get("url")
    if not stream_url:
        raise RuntimeError("Could not extract a stream URL from yt-dlp output.")

    return stream_url


def capture_frames(
    stream_url: str,
    timestamps: list[float],
    output_dir: Path,
    video_id: str,
) -> list[Path]:
    """Seek to each timestamp in the video stream and save a JPEG frame.

    Uses OpenCV (opencv-python-headless) to open the stream URL and seek to
    each position without downloading the full video.

    Returns the list of saved JPEG paths.
    """
    try:
        import cv2  # type: ignore
    except ImportError:
        raise RuntimeError(
            "opencv-python-headless is not installed. Run: pip install opencv-python-headless"
        )

    cap = cv2.VideoCapture(stream_url)
    if not cap.isOpened():
        raise RuntimeError(
            "OpenCV could not open the video stream. "
            "The stream URL may have expired — try running again."
        )

    saved: list[Path] = []
    total = len(timestamps)

    try:
        for i, ts in enumerate(timestamps, 1):
            ms = ts * 1000.0
            cap.set(cv2.CAP_PROP_POS_MSEC, ms)
            ret, frame = cap.read()
            if not ret or frame is None:
                print(f"   ⚠️   Frame {i}/{total} at {ts:.1f}s — could not read, skipping.")
                continue

            frame_path = output_dir / f"{video_id}_frame_{i:02d}.jpg"
            ok = cv2.imwrite(str(frame_path), frame, [cv2.IMWRITE_JPEG_QUALITY, 92])
            if ok:
                mins, secs = divmod(int(ts), 60)
                print(f"   ✓ Frame {i}/{total}  [{mins:02d}:{secs:02d}]  → {frame_path.name}")
                saved.append(frame_path)
            else:
                print(f"   ✗ Frame {i}/{total} at {ts:.1f}s — failed to write JPEG.")
    finally:
        cap.release()

    return saved


# ---------------------------------------------------------------------------
# HTML page generation
# ---------------------------------------------------------------------------

_PAGE_CSS = """\
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
:root {
  --bg:      #0d1117;
  --surface: #161b22;
  --border:  #30363d;
  --accent:  #58a6ff;
  --text:    #e6edf3;
  --muted:   #8b949e;
  --code-bg: #1e2433;
  --radius:  10px;
  --ph:      72px;
}
html { scroll-behavior: smooth; }
body {
  background: var(--bg);
  color: var(--text);
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
  font-size: 16px;
  line-height: 1.75;
}
body.has-player { padding-bottom: calc(var(--ph) + 8px); }
.page-header {
  background: linear-gradient(135deg, #1a1f35 0%, #0d1117 100%);
  border-bottom: 1px solid var(--border);
  padding: 36px 24px;
  text-align: center;
}
.page-header h1 { font-size: clamp(1.4rem, 4vw, 2.2rem); font-weight: 700; letter-spacing: -0.02em; }
.vid-badge { display: inline-block; margin-top: 6px; font-size: 0.75rem; color: var(--muted); font-family: "SF Mono", Consolas, monospace; }
.vid-badge a { color: inherit; text-decoration: none; border-bottom: 1px dotted currentColor; }
.vid-badge a:hover { opacity: .8; }
.source-link {
  display: inline-block; margin-top: 14px; padding: 6px 16px;
  background: rgba(88,166,255,.1); color: var(--accent);
  border: 1px solid rgba(88,166,255,.3); border-radius: 20px;
  font-size: 0.84rem; text-decoration: none; transition: background .2s;
}
.source-link:hover { background: rgba(88,166,255,.2); }
.content { max-width: 860px; margin: 0 auto; padding: 32px 24px; }
.section { margin-bottom: 48px; }
.section-heading {
  font-size: 0.75rem; font-weight: 600; color: var(--muted);
  text-transform: uppercase; letter-spacing: .1em;
  margin-bottom: 16px; padding-bottom: 8px; border-bottom: 1px solid var(--border);
}
.prose h1, .prose h2, .prose h3, .prose h4 { font-weight: 600; line-height: 1.3; margin: 1.5em 0 .5em; color: var(--text); }
.prose h1 { font-size: 1.7rem; }
.prose h2 { font-size: 1.3rem; color: var(--accent); }
.prose h3 { font-size: 1.1rem; }
.prose p { margin-bottom: 1em; }
.prose ul, .prose ol { padding-left: 1.6em; margin-bottom: 1em; }
.prose li { margin-bottom: .3em; }
.prose strong { font-weight: 600; }
.prose code { font-family: "SF Mono", Consolas, monospace; font-size: .875em; background: var(--code-bg); padding: .15em .4em; border-radius: 4px; color: #79c0ff; }
.prose pre { background: var(--code-bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 16px; overflow-x: auto; margin-bottom: 1em; }
.prose pre code { background: none; padding: 0; }
.prose blockquote { border-left: 3px solid var(--accent); padding: 8px 16px; margin: 0 0 1em; color: var(--muted); background: rgba(88,166,255,.06); border-radius: 0 4px 4px 0; }
.prose table { width: 100%; border-collapse: collapse; margin-bottom: 1em; font-size: .9em; }
.prose th, .prose td { padding: 8px 12px; border: 1px solid var(--border); text-align: left; }
.prose th { background: var(--surface); font-weight: 600; }
.prose tr:nth-child(even) { background: rgba(255,255,255,.02); }
.prose a { color: var(--accent); text-decoration: none; }
.prose a:hover { text-decoration: underline; }
.prose hr { border: none; border-top: 1px solid var(--border); margin: 24px 0; }
.md-fallback { white-space: pre-wrap; font-size: .88em; color: var(--muted); }
.gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
.gal-item { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); overflow: hidden; transition: border-color .2s, transform .2s; }
.gal-item:hover { border-color: var(--accent); transform: translateY(-2px); }
.gal-item img { width: 100%; display: block; aspect-ratio: 16/9; object-fit: cover; cursor: zoom-in; }
.gal-item figcaption { padding: 6px 10px; font-size: .72rem; color: var(--muted); font-family: "SF Mono", Consolas, monospace; }
.transcript-details { margin-top: 8px; }
.transcript-summary { cursor: pointer; color: var(--accent); font-size: 0.9rem; padding: 8px 0; list-style: none; user-select: none; }
.transcript-summary::-webkit-details-marker { display: none; }
.transcript-summary::marker { display: none; }
.transcript-summary::before { content: "▶ "; font-size: .7em; }
details[open] .transcript-summary::before { content: "▼ "; }
.transcript-body { margin-top: 12px; display: flex; flex-direction: column; gap: 3px; max-height: 420px; overflow-y: auto; padding: 14px 16px; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); }
.ts-line { font-size: .875rem; line-height: 1.55; color: var(--text); }
.ts-link { color: var(--accent); text-decoration: none; font-family: "SF Mono", Consolas, monospace; font-size: .8rem; }
.ts-link:hover { text-decoration: underline; }
.lightbox { display: none; position: fixed; inset: 0; background: rgba(0,0,0,.88); z-index: 2000; align-items: center; justify-content: center; cursor: zoom-out; }
.lightbox.open { display: flex; }
.lightbox img { max-width: 92vw; max-height: 88vh; object-fit: contain; border-radius: var(--radius); box-shadow: 0 8px 48px rgba(0,0,0,.7); }
.lightbox-close { position: absolute; top: 16px; right: 20px; font-size: 2rem; color: #fff; cursor: pointer; line-height: 1; opacity: .7; transition: opacity .15s; }
.lightbox-close:hover { opacity: 1; }
.player { position: fixed; bottom: 0; left: 0; right: 0; height: var(--ph); background: rgba(13,17,23,.93); backdrop-filter: blur(16px); -webkit-backdrop-filter: blur(16px); border-top: 1px solid var(--border); z-index: 1000; }
.player-inner { max-width: 860px; margin: 0 auto; height: 100%; display: flex; align-items: center; gap: 14px; padding: 0 24px; }
.play-btn { flex-shrink: 0; width: 42px; height: 42px; border-radius: 50%; background: var(--accent); border: none; cursor: pointer; display: flex; align-items: center; justify-content: center; color: #0d1117; transition: background .15s, transform .1s; }
.play-btn:hover { background: #79bbff; }
.play-btn:active { transform: scale(.94); }
.track-meta { flex-shrink: 0; max-width: 200px; overflow: hidden; }
.track-name { font-size: .78rem; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-family: "SF Mono", Consolas, monospace; }
.seek-wrap { flex: 1; display: flex; align-items: center; gap: 10px; }
.time-lbl { font-size: .76rem; color: var(--muted); font-variant-numeric: tabular-nums; min-width: 34px; font-family: "SF Mono", Consolas, monospace; }
.seek-bar { flex: 1; -webkit-appearance: none; appearance: none; height: 4px; border-radius: 2px; background: var(--border); cursor: pointer; outline: none; }
.seek-bar::-webkit-slider-thumb { -webkit-appearance: none; width: 14px; height: 14px; border-radius: 50%; background: var(--accent); cursor: pointer; transition: transform .1s; }
.seek-bar:hover::-webkit-slider-thumb { transform: scale(1.3); }
.seek-bar::-moz-range-thumb { width: 14px; height: 14px; border: none; border-radius: 50%; background: var(--accent); cursor: pointer; }
@media (max-width: 600px) {
  .track-meta { display: none; }
  .page-header { padding: 20px 16px; }
  .content { padding: 20px 16px; }
}"""

_PAGE_JS = """\
(function () {
  var audio = document.getElementById('audioEl');
  if (!audio) return;
  var playBtn   = document.getElementById('playBtn');
  var seekBar   = document.getElementById('seekBar');
  var curTime   = document.getElementById('currentTime');
  var totTime   = document.getElementById('totalTime');
  var iconPlay  = document.getElementById('iconPlay');
  var iconPause = document.getElementById('iconPause');
  function fmt(s) {
    if (isNaN(s) || !isFinite(s)) return '0:00';
    var m = Math.floor(s / 60), sec = Math.floor(s % 60);
    return m + ':' + (sec < 10 ? '0' : '') + sec;
  }
  playBtn.addEventListener('click', function () {
    audio.paused ? audio.play() : audio.pause();
  });
  audio.addEventListener('play', function () {
    iconPlay.setAttribute('hidden', '');
    iconPause.removeAttribute('hidden');
  });
  audio.addEventListener('pause', function () {
    iconPlay.removeAttribute('hidden');
    iconPause.setAttribute('hidden', '');
  });
  audio.addEventListener('ended', function () {
    iconPlay.removeAttribute('hidden');
    iconPause.setAttribute('hidden', '');
    seekBar.value = 0;
    curTime.textContent = '0:00';
  });
  audio.addEventListener('loadedmetadata', function () {
    totTime.textContent = fmt(audio.duration);
  });
  audio.addEventListener('timeupdate', function () {
    if (!audio.duration) return;
    seekBar.value = (audio.currentTime / audio.duration) * 100;
    curTime.textContent = fmt(audio.currentTime);
  });
  seekBar.addEventListener('input', function () {
    if (audio.duration) audio.currentTime = (seekBar.value / 100) * audio.duration;
  });
  document.addEventListener('keydown', function (e) {
    if (e.code === 'Space' && e.target.tagName !== 'INPUT' && e.target.tagName !== 'TEXTAREA') {
      e.preventDefault();
      audio.paused ? audio.play() : audio.pause();
    }
  });
}());

(function () {
  var lb    = document.getElementById('lightbox');
  var lbImg = document.getElementById('lightboxImg');
  if (!lb) return;

  function open(src) { lbImg.src = src; lb.classList.add('open'); }
  function close()   { lb.classList.remove('open'); lbImg.src = ''; }

  document.querySelectorAll('.gal-item img').forEach(function (img) {
    img.addEventListener('click', function () { open(img.src); });
  });

  lb.addEventListener('click', function (e) {
    if (e.target === lb || e.target.classList.contains('lightbox-close')) close();
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') close();
  });
}());
"""


def generate_page(
    video_id: str,
    url: str,
    output_dir: Path,
    audio_path: Path | None = None,
    image_paths: list[Path] | None = None,
    md_path: Path | None = None,
    title: str | None = None,
    base_name: str | None = None,
    transcript_path: Path | None = None,
) -> Path:
    """Generate a self-contained HTML page bundling the summary, images, and audio.

    All assets (audio and images) are base64-embedded, making the page fully
    portable — no web server or external dependencies required.  Missing assets
    are handled gracefully: sections are omitted when files don't exist.

    Returns the path to the saved HTML file.
    """
    import base64
    import html as _html

    # --- Render summary markdown ---
    summary_html = ""
    if md_path and md_path.exists():
        raw_md = md_path.read_text(encoding="utf-8")
        try:
            import markdown as md_lib
            summary_html = md_lib.markdown(raw_md, extensions=["tables", "fenced_code"])
        except ImportError:
            summary_html = f'<pre class="md-fallback">{_html.escape(raw_md)}</pre>'

    # --- Embed audio ---
    audio_element = ""
    audio_fname = ""
    has_audio = False
    if audio_path and audio_path.exists():
        audio_b64 = base64.b64encode(audio_path.read_bytes()).decode("ascii")
        audio_mime = "audio/wav" if audio_path.suffix.lower() == ".wav" else "audio/mpeg"
        audio_fname = _html.escape(audio_path.name)
        audio_element = (
            f'<audio id="audioEl" preload="metadata">'
            f'<source src="data:{audio_mime};base64,{audio_b64}" type="{audio_mime}">'
            f'</audio>'
        )
        has_audio = True

    # --- Embed images ---
    gallery_section = ""
    if image_paths:
        gal_items = []
        for img_path in image_paths:
            if img_path.exists():
                img_b64 = base64.b64encode(img_path.read_bytes()).decode("ascii")
                fname = _html.escape(img_path.name)
                gal_items.append(
                    f'<figure class="gal-item">'
                    f'<img src="data:image/jpeg;base64,{img_b64}" alt="{fname}" loading="lazy">'
                    f'<figcaption>{fname}</figcaption>'
                    f'</figure>'
                )
        if gal_items:
            gallery_section = (
                '\n<section class="section">'
                '\n<h2 class="section-heading">Key Moments</h2>'
                '\n<div class="gallery">\n'
                + "\n".join(gal_items)
                + '\n</div>\n</section>'
            )

    # --- Build transcript section with clickable timestamps ---
    transcript_section = ""
    if transcript_path and transcript_path.exists():
        ts_pattern = re.compile(r"^\[(\d{2}):(\d{2})\]\s*(.*)")
        ts_items = []
        for line in transcript_path.read_text(encoding="utf-8").splitlines():
            m = ts_pattern.match(line.strip())
            if m:
                mins, secs, text = int(m.group(1)), int(m.group(2)), m.group(3).strip()
                total_secs = mins * 60 + secs
                yt_ts_url = f"https://www.youtube.com/watch?v={video_id}&t={total_secs}"
                ts_items.append(
                    f'<span class="ts-line">'
                    f'<a class="ts-link" href="{_html.escape(yt_ts_url)}"'
                    f' target="podslacker-yt" rel="noopener">[{mins:02d}:{secs:02d}]</a>'
                    f' {_html.escape(text)}'
                    f'</span>'
                )
        if ts_items:
            transcript_section = (
                '\n<section class="section">'
                '\n<details class="transcript-details">'
                '\n<summary class="transcript-summary">'
                'To see the full transcript, click here'
                '</summary>'
                '\n<div class="transcript-body">\n'
                + "\n".join(ts_items)
                + '\n</div>\n</details>\n</section>'
            )

    # --- Audio player markup ---
    player_block = ""
    if has_audio:
        player_block = (
            '\n<div class="player">'
            '\n  <div class="player-inner">'
            '\n    <button class="play-btn" id="playBtn"'
            ' aria-label="Play / Pause" title="Play / Pause (Space)">'
            '\n      <svg id="iconPlay" viewBox="0 0 24 24" fill="currentColor"'
            ' width="24" height="24"><path d="M8 5v14l11-7z"/></svg>'
            '\n      <svg id="iconPause" viewBox="0 0 24 24" fill="currentColor"'
            ' width="24" height="24" hidden>'
            '<path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>'
            '\n    </button>'
            f'\n    <div class="track-meta">'
            f'<span class="track-name">{audio_fname}</span></div>'
            '\n    <div class="seek-wrap">'
            '\n      <span class="time-lbl" id="currentTime">0:00</span>'
            '\n      <input type="range" id="seekBar" class="seek-bar"'
            ' min="0" max="100" step="0.1" value="0">'
            '\n      <span class="time-lbl" id="totalTime">0:00</span>'
            '\n    </div>'
            '\n  </div>'
            f'\n  {audio_element}'
            '\n</div>'
        )

    url_esc = _html.escape(url)
    vid_esc = _html.escape(video_id)
    title_esc = _html.escape(title) if title else None
    body_attrs = ' class="has-player"' if has_audio else ""
    ps_link = '<a href="https://podslacker.com" target="_blank" rel="noopener">PodSlacker</a>'
    page_title_tag = f"PodSlacker — {title_esc or vid_esc}"
    header_h1 = title_esc or f"Created with {ps_link}"
    header_sub = (
        f'  <div class="vid-badge">{vid_esc}</div><br>\n'
        if not title_esc
        else f'  <div class="vid-badge">Created with {ps_link} &nbsp;·&nbsp; {vid_esc}</div><br>\n'
    )

    parts = [
        "<!DOCTYPE html>\n",
        '<html lang="en">\n<head>\n',
        '<meta charset="UTF-8">\n',
        '<meta name="viewport" content="width=device-width, initial-scale=1.0">\n',
        f"<title>{page_title_tag}</title>\n",
        "<style>\n",
        _PAGE_CSS,
        "\n</style>\n</head>\n",
        f"<body{body_attrs}>\n\n",
        '<header class="page-header">\n',
        f"  <h1>{header_h1}</h1>\n",
        header_sub,
        f'  <a class="source-link" href="{url_esc}" target="_blank"'
        f' rel="noopener">Watch on YouTube &#8599;</a>\n',
        "</header>\n\n",
        '<div class="content">\n',
        gallery_section,
        '\n<section class="section">\n',
        '<h2 class="section-heading">Summary &amp; Script</h2>\n',
        f'<div class="prose">{summary_html}</div>\n',
        "</section>",
        transcript_section,
        "\n</div>\n",
        player_block,
        '\n\n<div id="lightbox" class="lightbox" role="dialog" aria-modal="true">'
        '<span class="lightbox-close" aria-label="Close">&times;</span>'
        '<img id="lightboxImg" src="" alt="Full size frame">'
        '</div>',
        "\n\n<script>\n",
        _PAGE_JS,
        "</script>\n\n</body>\n</html>\n",
    ]

    page_path = output_dir / f"{base_name or video_id}_page.html"
    page_path.write_text("".join(parts), encoding="utf-8")
    return page_path


# ---------------------------------------------------------------------------
# GitHub Pages publishing
# ---------------------------------------------------------------------------

def publish_to_github(
    page_path: Path,
    repo_name: str = "podslacker-pages",
    token_env: str = "GITHUB_TOKEN",
    branch: str = "gh-pages",
) -> str:
    """Upload an HTML page to GitHub Pages using the GitHub REST API.

    Creates the repository if it doesn't exist (public, auto-initialised).
    Creates the target branch if it doesn't exist (from the default branch).
    Creates or updates the page file, then enables GitHub Pages if needed.

    Returns the public GitHub Pages URL for the uploaded page.
    Raises RuntimeError on any unrecoverable API failure.
    """
    import base64
    import time
    import requests as _req

    token = os.environ.get(token_env)
    if not token:
        raise RuntimeError(
            f"GitHub token not found. Set '{token_env}' to a Personal Access Token "
            "with 'repo' and 'pages' scopes. "
            "Create one at: https://github.com/settings/tokens"
        )

    hdrs = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    api = "https://api.github.com"

    def _get(path: str, **kw):
        return _req.get(f"{api}{path}", headers=hdrs, timeout=15, **kw)

    def _post(path: str, **kw):
        return _req.post(f"{api}{path}", headers=hdrs, timeout=15, **kw)

    def _put(path: str, **kw):
        return _req.put(f"{api}{path}", headers=hdrs, timeout=30, **kw)

    # 1. Authenticate
    me = _get("/user")
    me.raise_for_status()
    username = me.json()["login"]
    print(f"   Authenticated as: {username}")

    # 2. Create repo if needed
    repo_check = _get(f"/repos/{username}/{repo_name}")
    if repo_check.status_code == 404:
        print(f"   Creating repository '{repo_name}'…")
        r = _post(
            "/user/repos",
            json={
                "name": repo_name,
                "description": "PodSlacker generated podcast pages",
                "private": False,
                "auto_init": True,
            },
        )
        r.raise_for_status()
        print(f"   ✓ Repository created: github.com/{username}/{repo_name}")
        time.sleep(2)  # let GitHub finish initialising
    else:
        repo_check.raise_for_status()
        print(f"   ✓ Repository: github.com/{username}/{repo_name}")

    # 3. Get default branch
    repo_info = _get(f"/repos/{username}/{repo_name}")
    repo_info.raise_for_status()
    default_branch = repo_info.json().get("default_branch", "main")

    # 4. Ensure the publish branch exists
    branch_check = _get(f"/repos/{username}/{repo_name}/branches/{branch}")
    if branch_check.status_code == 404:
        ref_r = _get(f"/repos/{username}/{repo_name}/git/ref/heads/{default_branch}")
        ref_r.raise_for_status()
        sha = ref_r.json()["object"]["sha"]
        cb = _post(
            f"/repos/{username}/{repo_name}/git/refs",
            json={"ref": f"refs/heads/{branch}", "sha": sha},
        )
        cb.raise_for_status()
        print(f"   ✓ Created branch: {branch}")
    else:
        branch_check.raise_for_status()
        print(f"   ✓ Branch: {branch}")

    # 5. Create or update the file
    filename = page_path.name
    file_content_b64 = base64.b64encode(page_path.read_bytes()).decode("ascii")
    file_api_path = f"/repos/{username}/{repo_name}/contents/{filename}"

    existing = _get(file_api_path, params={"ref": branch})
    put_body: dict = {
        "message": f"Update {filename} via podslacker",
        "content": file_content_b64,
        "branch": branch,
    }
    if existing.status_code == 200:
        put_body["sha"] = existing.json()["sha"]
        action = "Updating"
    else:
        action = "Uploading"
    print(f"   {action} {filename}…")
    put_r = _put(file_api_path, json=put_body)
    put_r.raise_for_status()
    print(f"   ✓ File published to branch '{branch}'")

    # 6. Enable GitHub Pages if not already configured
    pages_check = _get(f"/repos/{username}/{repo_name}/pages")
    if pages_check.status_code == 404:
        enable_r = _post(
            f"/repos/{username}/{repo_name}/pages",
            json={"source": {"branch": branch, "path": "/"}},
        )
        if enable_r.status_code in (201, 204):
            print("   ✓ GitHub Pages enabled")
        else:
            print(
                f"   ⚠️  Could not enable GitHub Pages automatically "
                f"(HTTP {enable_r.status_code})."
            )
            print(
                f"      Visit https://github.com/{username}/{repo_name}/settings/pages "
                f"and set the source to branch '{branch}'."
            )
    else:
        print("   ✓ GitHub Pages already enabled")

    return f"https://{username}.github.io/{repo_name}/{filename}"


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def _build_step_client(
    base_url: str | None,
    api_key_env: str | None,
    fallback_base_url: str | None,
    fallback_api_key_env: str,
    fallback_client,
    label: str,
):
    """Return an OpenAI client for a pipeline step, reusing the fallback when
    the step's settings are identical to the base LLM config.

    base_url / api_key_env are the step-specific overrides (may be None).
    fallback_* are the base --llm-* values.
    label is used in error messages only.
    """
    from openai import OpenAI

    effective_url     = base_url     or fallback_base_url
    effective_key_env = api_key_env  or fallback_api_key_env

    # If nothing differs from the base client, reuse it directly.
    if effective_url == fallback_base_url and effective_key_env == fallback_api_key_env:
        return fallback_client

    api_key = _require_env(effective_key_env, f"{label} API key")
    kwargs: dict = {"api_key": api_key}
    if effective_url:
        kwargs["base_url"] = effective_url
    return OpenAI(**kwargs)


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
    parser.add_argument(
        "--host1-name",
        default="ALEX",
        metavar="NAME",
        help=(
            "Name of the first podcast host, used in the script and audio generation. "
            "In --hosts 1 (monologue) mode this is the sole narrator. Default: ALEX"
        ),
    )
    parser.add_argument(
        "--host2-name",
        default="JORDAN",
        metavar="NAME",
        help=(
            "Name of the second podcast host. Only applies in --hosts 2 (dialogue) mode. "
            "Default: JORDAN"
        ),
    )
    parser.add_argument(
        "--num-frames",
        type=int,
        default=6,
        metavar="N",
        help=(
            "Number of key-moment frames to capture from the video and save as JPEGs. "
            "The LLM identifies the most visually significant timestamps automatically. "
            "Set to 0 to disable frame capture. Default: 5. "
            "Requires: pip install yt-dlp opencv-python-headless"
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
    prompt_group.add_argument(
        "--key-moments-prompt",
        metavar="FILE",
        default=None,
        help=(
            "System prompt for the key-moment identification step (default: prompts/key_moments.txt). "
            "The placeholder {num_frames} in the file is replaced with the value of --num-frames."
        ),
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
    llm_group.add_argument(
        "--key-moments-model",
        default="openrouter/auto:free",
        metavar="MODEL",
        help=(
            "Model to use for key-moment timestamp identification. "
            "Default: openrouter/auto:free (OpenRouter's free-tier auto-routing). "
            "Set --llm-base-url https://openrouter.ai/api/v1 and "
            "--llm-api-key-env OPENROUTER_API_KEY when using this default. "
            "Any model works here — the task is structured JSON output rather than "
            "creative writing, so a smaller/cheaper model is fine."
        ),
    )

    # ---- Per-step LLM overrides ----
    step_group = parser.add_argument_group(
        "Per-step LLM overrides",
        "Each pipeline step (summary, script, key-moments) can use a completely "
        "independent model and API provider. Any value left unset inherits from "
        "the base --llm-* flags above.",
    )
    # Summary
    step_group.add_argument(
        "--summary-model",
        default=None, metavar="MODEL",
        help="Model for the markdown summary step. Defaults to --llm-model.",
    )
    step_group.add_argument(
        "--summary-base-url",
        default=None, metavar="URL",
        help="API base URL for the summary step. Defaults to --llm-base-url.",
    )
    step_group.add_argument(
        "--summary-api-key-env",
        default=None, metavar="VAR",
        help="Env var holding the API key for the summary step. Defaults to --llm-api-key-env.",
    )
    # Script (dialogue / monologue)
    step_group.add_argument(
        "--script-model",
        default=None, metavar="MODEL",
        help="Model for the podcast script step. Defaults to --llm-model.",
    )
    step_group.add_argument(
        "--script-base-url",
        default=None, metavar="URL",
        help="API base URL for the script step. Defaults to --llm-base-url.",
    )
    step_group.add_argument(
        "--script-api-key-env",
        default=None, metavar="VAR",
        help="Env var holding the API key for the script step. Defaults to --llm-api-key-env.",
    )
    # Key moments
    step_group.add_argument(
        "--key-moments-base-url",
        default=None, metavar="URL",
        help="API base URL for the key-moments step. Defaults to --llm-base-url.",
    )
    step_group.add_argument(
        "--key-moments-api-key-env",
        default=None, metavar="VAR",
        help="Env var holding the API key for the key-moments step. Defaults to --llm-api-key-env.",
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
        "--voice-host1",
        default="onyx",
        choices=["alloy", "echo", "fable", "onyx", "nova", "shimmer"],
        help="OpenAI voice for host 1 (--host1-name). Default: onyx",
    )
    tts_group.add_argument(
        "--voice-host2",
        default="nova",
        choices=["alloy", "echo", "fable", "onyx", "nova", "shimmer"],
        help="OpenAI voice for host 2 (--host2-name). Only applies in --hosts 2 mode. Default: nova",
    )

    # ---- Kokoro TTS flags ----
    kokoro_group = parser.add_argument_group(
        "Kokoro TTS options",
        "Used when --tts-engine kokoro. Runs locally on CPU; no API key required. "
        "Model weights (~82 MB) are downloaded automatically from HuggingFace on first run.",
    )
    kokoro_group.add_argument(
        "--kokoro-voice-host1",
        default="am_michael",
        metavar="VOICE",
        help=(
            "Kokoro voice ID for host 1 (--host1-name). Default: am_michael (American male). "
            "American male: am_michael, am_adam. "
            "American female: af_heart, af_bella, af_nicole, af_sarah, af_sky. "
            "British (use --kokoro-lang b): bm_george, bm_lewis, bf_emma, bf_isabella."
        ),
    )
    kokoro_group.add_argument(
        "--kokoro-voice-host2",
        default="af_heart",
        metavar="VOICE",
        help=(
            "Kokoro voice ID for host 2 (--host2-name). Default: af_heart (American female). "
            "In --hosts 1 mode this flag is unused."
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

    # ---- HTML page & GitHub Pages ----
    page_group = parser.add_argument_group(
        "HTML page & GitHub Pages",
        "Controls generation and optional publishing of a self-contained HTML podcast page.",
    )
    page_group.add_argument(
        "--no-page",
        action="store_true",
        help=(
            "Skip HTML page generation. By default podslacker produces a self-contained "
            "<video_id>_page.html file in --output-dir that embeds the summary, "
            "key-moment images, and audio player in one portable file."
        ),
    )
    page_group.add_argument(
        "--publish-github",
        action="store_true",
        help=(
            "Publish the generated HTML page to GitHub Pages. "
            "Requires a GitHub Personal Access Token (with 'repo' and 'pages' scopes) "
            "stored in the env var named by --github-token-env."
        ),
    )
    page_group.add_argument(
        "--github-repo",
        default="podslacker-pages",
        metavar="REPO",
        help=(
            "GitHub repository name to publish pages to. "
            "Created automatically if it doesn't exist. Default: podslacker-pages"
        ),
    )
    page_group.add_argument(
        "--github-token-env",
        default="GITHUB_TOKEN",
        metavar="VAR",
        help=(
            "Name of the environment variable that holds the GitHub Personal Access Token. "
            "Default: GITHUB_TOKEN"
        ),
    )
    page_group.add_argument(
        "--github-branch",
        default="gh-pages",
        metavar="BRANCH",
        help="Branch in the GitHub repository to publish to. Default: gh-pages",
    )

    # ---- Config file ----
    parser.add_argument(
        "--config",
        metavar="FILE",
        default=None,
        help=(
            f"Path to a JSON configuration file. "
            f"Defaults are loaded from podslacker.json next to the script if it exists. "
            f"CLI flags always override config file values."
        ),
    )

    # Two-pass parse: first extract --config (if given), load the file,
    # inject its values as argparse defaults, then do the real parse.
    pre, _ = parser.parse_known_args()
    config_path = Path(pre.config) if pre.config else _DEFAULT_CONFIG_PATH
    config = load_config(config_path)
    if config:
        label = pre.config or f"{config_path} (auto)"
        print(f"\n⚙️   Loaded config: {label}  ({len(config)} setting(s))")
        parser.set_defaults(**config)

    args = parser.parse_args()

    from openai import OpenAI

    # ---- Build base LLM client ----
    llm_api_key = _require_env(args.llm_api_key_env, "LLM API key")
    llm_kwargs: dict = {"api_key": llm_api_key}
    if args.llm_base_url:
        llm_kwargs["base_url"] = args.llm_base_url
    llm_client = OpenAI(**llm_kwargs)

    # ---- Build per-step clients (reuse base client when settings are identical) ----
    summary_client = _build_step_client(
        args.summary_base_url, args.summary_api_key_env,
        args.llm_base_url, args.llm_api_key_env, llm_client, "summary",
    )
    script_client = _build_step_client(
        args.script_base_url, args.script_api_key_env,
        args.llm_base_url, args.llm_api_key_env, llm_client, "script",
    )
    key_moments_client = _build_step_client(
        args.key_moments_base_url, args.key_moments_api_key_env,
        args.llm_base_url, args.llm_api_key_env, llm_client, "key-moments",
    )

    summary_model      = args.summary_model      or args.llm_model
    script_model       = args.script_model       or args.llm_model
    key_moments_model  = args.key_moments_model  or args.llm_model

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
        transcript, timed_entries = fetch_transcript(
            video_id, cookies_file=args.cookies, proxy_url=args.proxy
        )
        print(f"   ✓ Got transcript ({len(transcript):,} characters, {len(timed_entries):,} segments)")
    except Exception as exc:
        print(f"   ✗ {exc}")
        sys.exit(1)

    # Fetch the human-readable video title and build a descriptive file prefix.
    video_title = fetch_video_title(args.url)
    if video_title:
        print(f"   ✓ Title: {video_title}")
        base_name = f"{sanitize_title(video_title)}_{video_id}"
    else:
        print("   ⚠️   Could not fetch video title — using video ID only.")
        base_name = video_id

    transcript_path = output_dir / f"{base_name}_transcript.md"
    timestamped_lines = []
    for start, text in timed_entries:
        mins, secs = divmod(int(start), 60)
        timestamped_lines.append(f"[{mins:02d}:{secs:02d}] {text.strip()}")
    transcript_md = (
        f"# Transcript{': ' + video_title if video_title else ''}\n\n"
        f"**Source:** {args.url}  \n"
        f"**Video ID:** `{video_id}`\n\n"
        f"---\n\n"
        + "\n".join(timestamped_lines)
        + "\n"
    )
    transcript_path.write_text(transcript_md, encoding="utf-8")
    print(f"   ✓ Transcript saved → {transcript_path}")

    # ---- Step 2: AI content (or load from cache) ----
    md_path = output_dir / f"{base_name}_summary.md"

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
        host_label = "solo" if args.hosts == 1 else "two-host"
        summary_label      = args.summary_base_url      or args.llm_base_url or "OpenAI"
        script_label       = args.script_base_url       or args.llm_base_url or "OpenAI"
        print(f"\n🤖  Generating content  [{host_label} podcast]")
        summary_prompt   = load_prompt("summary",   args.summary_prompt)
        dialogue_prompt  = load_prompt("dialogue",  args.dialogue_prompt)
        monologue_prompt = load_prompt("monologue", args.monologue_prompt)
        try:
            print(f"   [{summary_label} / {summary_model}]")
            markdown_summary = generate_summary(
                summary_client, transcript, args.url,
                model=summary_model,
                summary_prompt=summary_prompt,
            )
            print(f"   ✓ Summary: {len(markdown_summary):,} characters")

            print(f"   [{script_label} / {script_model}]")
            dialogue_segments = generate_script(
                script_client, transcript, args.url,
                model=script_model,
                hosts=args.hosts,
                host1_name=args.host1_name,
                host2_name=args.host2_name,
                dialogue_prompt=dialogue_prompt,
                monologue_prompt=monologue_prompt,
            )
            print(f"   ✓ Script: {len(dialogue_segments)} segments")
        except Exception as exc:
            print(f"   ✗ {exc}")
            sys.exit(1)

        # ---- Step 3: Save markdown ----
        md_content = build_markdown(
            args.url, video_id, markdown_summary, dialogue_segments, title=video_title
        )
        md_path.write_text(md_content, encoding="utf-8")
        print(f"\n📄  Markdown saved → {md_path}")

    # Initialise output-tracking variables used in Step 6
    audio_path: Path | None = None
    saved_frames: list[Path] = []

    # ---- Step 4: Audio ----
    if not args.no_audio:
        if args.tts_engine == "kokoro":
            audio_path = output_dir / f"{base_name}_podcast.wav"
            print(f"\n🎙️   Generating podcast audio  [Kokoro / local CPU]…")
            print(f"   Voices: {args.host1_name}={args.kokoro_voice_host1}, {args.host2_name}={args.kokoro_voice_host2}")
            try:
                generate_audio_kokoro(
                    dialogue_segments,
                    audio_path,
                    voice_host1=args.kokoro_voice_host1,
                    voice_host2=args.kokoro_voice_host2,
                    lang_code=args.kokoro_lang,
                    speed=args.kokoro_speed,
                    host1_name=args.host1_name,
                    host2_name=args.host2_name,
                )
                print(f"\n🎧  Podcast saved → {audio_path}  (WAV, 24 kHz mono)")
            except Exception as exc:
                print(f"   ✗ Audio generation failed: {exc}")
                sys.exit(1)
        else:
            audio_path = output_dir / f"{base_name}_podcast.mp3"
            print(f"\n🎙️   Generating podcast audio  [OpenAI TTS / {args.tts_model}]…")
            try:
                generate_audio(
                    tts_client,
                    dialogue_segments,
                    audio_path,
                    voice_host1=args.voice_host1,
                    voice_host2=args.voice_host2,
                    tts_model=args.tts_model,
                    host1_name=args.host1_name,
                    host2_name=args.host2_name,
                )
                print(f"\n🎧  Podcast saved → {audio_path}")
            except Exception as exc:
                print(f"   ✗ Audio generation failed: {exc}")
                sys.exit(1)

    # ---- Step 5: Frame capture ----
    if args.num_frames > 0 and timed_entries:
        effective_km_url = args.key_moments_base_url or args.llm_base_url
        if key_moments_model == "openrouter/auto:free" and not effective_km_url:
            print(
                "   ⚠️   --key-moments-model is set to openrouter/auto:free but "
                "no OpenRouter base URL is configured.\n"
                "      Add: --key-moments-base-url https://openrouter.ai/api/v1 "
                "--key-moments-api-key-env OPENROUTER_API_KEY\n"
                "      Or override with: --key-moments-model gpt-4o-mini"
            )
        key_moments_prompt = load_prompt("key_moments", args.key_moments_prompt)
        print(f"\n📸  Capturing {args.num_frames} key-moment frame(s)…")
        try:
            key_timestamps = identify_key_moments(
                key_moments_client,
                timed_entries,
                num_frames=args.num_frames,
                model=key_moments_model,
                url=args.url,
                system_prompt=key_moments_prompt,
            )
            ts_display = ", ".join(
                f"{int(t // 60):02d}:{int(t % 60):02d}" for t in key_timestamps
            )
            print(f"   Key moments identified: {ts_display}")

            print("   Resolving video stream URL via yt-dlp…")
            try:
                stream_url = get_video_stream_url(args.url)
                print("   ✓ Stream URL resolved")
            except RuntimeError as exc:
                print(f"   ✗ Could not resolve stream URL: {exc}")
                print("     Skipping frame capture.")
                stream_url = None

            if stream_url:
                saved_frames = capture_frames(stream_url, key_timestamps, output_dir, base_name)
                if saved_frames:
                    print(f"\n🖼️   {len(saved_frames)} frame(s) saved → {output_dir}/")
                else:
                    print("   ⚠️   No frames were saved (stream may not support seeking).")

        except Exception as exc:
            print(f"   ✗ Frame capture failed: {exc}")
            print("     Continuing without frames.")
    elif args.num_frames == 0:
        print("\n⏭️   Frame capture skipped (--num-frames 0)")

    # ---- Step 6: HTML page ----
    if not args.no_page:
        page_audio = audio_path if not args.no_audio and audio_path else None
        page_images = saved_frames if saved_frames else None
        print(f"\n🌐  Generating HTML page…")
        page_path_out: Path | None = None
        try:
            page_path_out = generate_page(
                video_id,
                args.url,
                output_dir,
                audio_path=page_audio,
                image_paths=page_images,
                md_path=md_path,
                title=video_title,
                base_name=base_name,
                transcript_path=transcript_path,
            )
            size_kb = page_path_out.stat().st_size // 1024
            print(f"   ✓ Page saved → {page_path_out}  ({size_kb:,} KB)")
        except Exception as exc:
            print(f"   ✗ HTML page generation failed: {exc}")
            print("     Continuing without page.")

        if page_path_out and args.publish_github:
            print(f"\n🚀  Publishing to GitHub Pages…")
            try:
                page_url = publish_to_github(
                    page_path_out,
                    repo_name=args.github_repo,
                    token_env=args.github_token_env,
                    branch=args.github_branch,
                )
                print(f"   ✓ Published → {page_url}")
                print("      (Note: GitHub Pages may take a minute or two to go live.)")
            except Exception as exc:
                print(f"   ✗ GitHub Pages publish failed: {exc}")
    else:
        print("\n⏭️   HTML page skipped (--no-page)")

    print("\n✅  All done!\n")


if __name__ == "__main__":
    main()
