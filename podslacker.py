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

    # Substitute {num_frames} placeholder so the prompt file can reference it.
    try:
        system = system_prompt.format(num_frames=num_frames)
    except KeyError as exc:
        raise RuntimeError(
            f"Key moments prompt contains an unknown placeholder: {exc}. "
            "Only {{num_frames}} is supported."
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
        default=5,
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

    transcript_path = output_dir / f"{video_id}_transcript.md"
    timestamped_lines = []
    for start, text in timed_entries:
        mins, secs = divmod(int(start), 60)
        timestamped_lines.append(f"[{mins:02d}:{secs:02d}] {text.strip()}")
    transcript_md = (
        f"# Transcript\n\n"
        f"**Source:** {args.url}  \n"
        f"**Video ID:** `{video_id}`\n\n"
        f"---\n\n"
        + "\n".join(timestamped_lines)
        + "\n"
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
        md_content = build_markdown(args.url, video_id, markdown_summary, dialogue_segments)
        md_path.write_text(md_content, encoding="utf-8")
        print(f"\n📄  Markdown saved → {md_path}")

    # ---- Step 4: Audio ----
    if not args.no_audio:
        if args.tts_engine == "kokoro":
            audio_path = output_dir / f"{video_id}_podcast.wav"
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
            audio_path = output_dir / f"{video_id}_podcast.mp3"
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
                saved_frames = capture_frames(stream_url, key_timestamps, output_dir, video_id)
                if saved_frames:
                    print(f"\n🖼️   {len(saved_frames)} frame(s) saved → {output_dir}/")
                else:
                    print("   ⚠️   No frames were saved (stream may not support seeking).")

        except Exception as exc:
            print(f"   ✗ Frame capture failed: {exc}")
            print("     Continuing without frames.")
    elif args.num_frames == 0:
        print("\n⏭️   Frame capture skipped (--num-frames 0)")

    print("\n✅  All done!\n")


if __name__ == "__main__":
    main()
