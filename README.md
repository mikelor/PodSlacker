# podslacker

Turn any YouTube video into a podcast. podslacker fetches a video's transcript, uses an LLM to write a markdown summary and a podcast script, then generates an MP3 using text-to-speech — all from the command line.

---

## Quick Start

```bash
# 1. Install dependencies
pip install youtube-transcript-api openai

# 2. Set your OpenAI API key
export OPENAI_API_KEY=sk-...

# 3. Run it
python podslacker.py https://www.youtube.com/watch?v=VIDEO_ID
```

Two files are created in the current directory:
- `<video_id>_summary.md` — structured markdown summary + podcast script
- `<video_id>_podcast.mp3` — the generated podcast audio

---

## Runtime Environment Setup

### Python version

Python 3.10 or later is required (the script uses `X | Y` union type hints).

### Dependencies

Install with pip:

```bash
pip install youtube-transcript-api openai
```

| Package | Version | Purpose |
|---|---|---|
| `youtube-transcript-api` | >= 1.0.0 | Fetches YouTube captions without the Data API |
| `openai` | >= 1.30.0 | LLM summarisation (chat completions) + TTS audio |

No system-level dependencies are needed. The script concatenates MP3 audio as raw bytes, so **ffmpeg is not required**.

### API keys

By default podslacker uses OpenAI for both the LLM and TTS steps. You will need an OpenAI API key:

```bash
export OPENAI_API_KEY=sk-...
```

If you use a different LLM provider (see the LLM flags section below), set that provider's key under whatever environment variable name you choose and point podslacker at it with `--llm-api-key-env`.

### Virtual environment (recommended)

```bash
python -m venv .venv
source .venv/bin/activate      # Windows: .venv\Scripts\activate
pip install youtube-transcript-api openai
```

---

## Command Line Arguments

```
python podslacker.py <url> [options]
```

### Positional argument

| Argument | Description |
|---|---|
| `url` | Full YouTube video URL (any standard format: `watch?v=`, `youtu.be/`, `/shorts/`, `/embed/`) |

### General options

| Flag | Default | Description |
|---|---|---|
| `--output-dir DIR`, `-o DIR` | `.` (current directory) | Directory where the markdown and MP3 files are saved. Created automatically if it doesn't exist. |
| `--hosts {1,2}` | `2` | Number of podcast hosts. `1` produces a solo monologue narrated by a single host. `2` produces a two-host dialogue between Alex and Jordan. |
| `--no-audio` | off | Skip TTS audio generation entirely. Only the markdown summary file is produced. Useful for a quick read-through or when you just want the written script. |
| `--reuse-summary` | off | If a summary markdown for this video already exists in `--output-dir`, skip the LLM call and read the script directly from that file. Saves LLM API costs when re-generating audio. Falls back to LLM generation if the file is not found. |

### YouTube access options

These flags help when YouTube blocks the transcript fetch (see [Working Around IP Bans](#working-around-ip-bans)).

| Flag | Default | Description |
|---|---|---|
| `--cookies FILE` | none | Path to a Netscape-format `cookies.txt` file exported from your browser while logged into YouTube. Authenticates requests as a real user, which usually resolves IP blocks. |
| `--proxy URL` | none | Proxy URL for transcript requests. Supports HTTP, HTTPS, and SOCKS5 formats, e.g. `http://user:pass@host:port` or `socks5://host:port`. |

### LLM provider options

podslacker uses the OpenAI chat completions API for summarisation and script generation. Any provider that exposes an OpenAI-compatible endpoint can be used as a drop-in replacement.

| Flag | Default | Description |
|---|---|---|
| `--llm-base-url URL` | OpenAI (`https://api.openai.com/v1`) | Base URL of the chat completions endpoint. Set this to use an alternative provider. |
| `--llm-model MODEL` | `gpt-4o` | Model name to request. Use the model identifier the provider expects. |
| `--llm-api-key-env VAR` | `OPENAI_API_KEY` | Name of the environment variable that holds the LLM API key. Keeping the key in an env var avoids it appearing in shell history. |

**Compatible providers (examples):**

| Provider | `--llm-base-url` | Example `--llm-model` |
|---|---|---|
| OpenAI (default) | *(omit)* | `gpt-4o`, `gpt-4o-mini` |
| OpenRouter | `https://openrouter.ai/api/v1` | `anthropic/claude-3-5-sonnet`, `google/gemini-pro` |
| Ollama (local) | `http://localhost:11434/v1` | `llama3.2`, `mistral` |
| Groq | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` |
| Together AI | `https://api.together.xyz/v1` | `meta-llama/Llama-3-70b-chat-hf` |

### Prompt customisation options

These flags let you swap out the system prompts sent to the LLM without editing the script. Each accepts a path to a plain text file. See [Customising Prompts](#customising-prompts) for the full priority rules and editing guidance.

| Flag | Default | Description |
|---|---|---|
| `--summary-prompt FILE` | `prompts/summary.txt` | System prompt for the markdown summary LLM call. |
| `--dialogue-prompt FILE` | `prompts/dialogue.txt` | System prompt for the two-host dialogue LLM call (used when `--hosts 2`). |
| `--monologue-prompt FILE` | `prompts/monologue.txt` | System prompt for the solo monologue LLM call (used when `--hosts 1`). |

### TTS provider options

Audio is always generated via OpenAI's TTS endpoint. These flags control which model and voices are used.

| Flag | Default | Description |
|---|---|---|
| `--tts-model MODEL` | `tts-1` | OpenAI TTS model. Use `tts-1-hd` for higher quality audio at a higher cost per character. |
| `--tts-api-key-env VAR` | `OPENAI_API_KEY` | Name of the env var holding the TTS API key. Only needed if your TTS key is different from your LLM key. |
| `--voice-alex VOICE` | `onyx` | Voice used for host Alex (solo host in `--hosts 1` mode, or the analytical host in two-host mode). |
| `--voice-jordan VOICE` | `nova` | Voice used for host Jordan (only applies in `--hosts 2` mode). |

**Available voices:** `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer`

---

## Usage Examples

**Default two-host podcast with OpenAI:**
```bash
export OPENAI_API_KEY=sk-...
python podslacker.py https://www.youtube.com/watch?v=VIDEO_ID
```

**Solo host, summary only (no audio):**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --hosts 1 --no-audio
```

**Save outputs to a specific folder:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --output-dir ~/podcasts
```

**Higher quality audio:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --tts-model tts-1-hd
```

**Regenerate audio without re-running the LLM:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --reuse-summary --output-dir ~/podcasts
```

**Use OpenRouter with Claude instead of OpenAI:**
```bash
export OPENROUTER_API_KEY=sk-or-...
python podslacker.py https://youtu.be/VIDEO_ID \
  --llm-base-url https://openrouter.ai/api/v1 \
  --llm-model anthropic/claude-3-5-sonnet \
  --llm-api-key-env OPENROUTER_API_KEY
```

**Use a local Ollama model (LLM is free; TTS still uses OpenAI):**
```bash
export OPENAI_API_KEY=sk-...
python podslacker.py https://youtu.be/VIDEO_ID \
  --llm-base-url http://localhost:11434/v1 \
  --llm-model llama3.2 \
  --llm-api-key-env OPENAI_API_KEY
```

**Use a custom dialogue prompt for a different show format:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --dialogue-prompt ~/my_prompts/debate_format.txt
```

**Override all three prompts at once:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --summary-prompt ~/prompts/brief_summary.txt \
  --dialogue-prompt ~/prompts/comedy_duo.txt \
  --monologue-prompt ~/prompts/news_anchor.txt
```

---

## Customising Prompts

The LLM calls that generate the markdown summary and the podcast script are driven by system prompts stored as plain text files. You can edit these freely without touching the Python code.

### Prompt files

The `prompts/` folder next to `podslacker.py` contains the three default prompt files:

| File | Used for |
|---|---|
| `prompts/summary.txt` | The markdown summary (both `--hosts` modes) |
| `prompts/dialogue.txt` | The two-host conversation (`--hosts 2`) |
| `prompts/monologue.txt` | The solo narration (`--hosts 1`) |

Edit any of these files to change the LLM's behaviour for every future run.

### Priority order

Each prompt is resolved using the following priority, from highest to lowest:

1. **CLI flag** — `--summary-prompt FILE`, `--dialogue-prompt FILE`, or `--monologue-prompt FILE`. If provided, this file is always used.
2. **Default prompt file** — the corresponding file in `prompts/` next to the script, if it exists and is non-empty.
3. **Built-in fallback** — the prompt string hardcoded inside `podslacker.py`, used automatically if both the CLI flag and the default file are absent or empty.

This means the script always works out of the box, even if the `prompts/` folder is missing.

### Tips for writing prompts

The dialogue prompt must produce lines in exactly this format for the parser to recognise them:

```
[ALEX]: <text>
[JORDAN]: <text>
```

The monologue prompt must produce lines in this format:

```
[HOST]: <text>
```

You can rename the hosts, change their personalities, adjust the number of exchanges, switch to a different language, or change the tone entirely — as long as the output lines start with the correct speaker tag. The summary prompt has no format constraints and can be customised freely.

---

## Working Around IP Bans

YouTube occasionally blocks transcript requests from certain IP ranges — particularly cloud provider IPs (AWS, GCP, Azure) or addresses that have made too many requests in a short window. This shows up as an error like:

```
YouTube is blocking requests from your IP.
```

There are two workarounds built into podslacker.

### Option 1: Browser cookies (recommended)

Passing cookies from a logged-in YouTube session authenticates the request as a real browser user, which YouTube is far less likely to block.

**Step 1 — Export your cookies:**

Install the [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc) Chrome extension (or an equivalent for Firefox). Navigate to [youtube.com](https://youtube.com) while logged in, click the extension, and save the file as `cookies.txt`.

**Step 2 — Pass the file to podslacker:**

```bash
python podslacker.py https://youtu.be/VIDEO_ID --cookies ~/cookies.txt
```

> **Note:** Cookies expire. If you start getting blocked again after a while, re-export a fresh `cookies.txt`.

### Option 2: Proxy

Route the transcript request through a residential or datacenter proxy.

```bash
# HTTP/HTTPS proxy
python podslacker.py https://youtu.be/VIDEO_ID \
  --proxy http://user:pass@proxyhost:8080

# SOCKS5 proxy
python podslacker.py https://youtu.be/VIDEO_ID \
  --proxy socks5://user:pass@proxyhost:1080
```

You can combine both flags if needed:

```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --cookies ~/cookies.txt \
  --proxy http://user:pass@proxyhost:8080
```

---

## Architecture and Components

podslacker is a single-file Python script with four clearly separated stages that run in sequence.

```
YouTube URL
    │
    ▼
┌─────────────────────┐
│  1. Transcript fetch │  youtube-transcript-api
└─────────────────────┘
    │  raw transcript text
    ▼
┌─────────────────────┐
│  2. LLM generation  │  OpenAI-compatible chat completions
└─────────────────────┘
    │  markdown summary + podcast script
    ▼
┌─────────────────────┐
│  3. File output     │  writes <video_id>_summary.md
└─────────────────────┘
    │  script segments
    ▼
┌─────────────────────┐
│  4. TTS audio       │  OpenAI speech endpoint
└─────────────────────┘
    │
    ▼
<video_id>_podcast.mp3
```

### Stage 1 — Transcript fetch (`fetch_transcript`)

Uses `youtube-transcript-api` to download the video's auto-generated or manually uploaded captions. The raw caption entries (each a short text snippet with a timestamp) are concatenated into a single string. If English captions are not available, the library falls back to any other available language. Optionally, a `requests.Session` loaded with browser cookies or a `GenericProxyConfig` is injected into the API client to work around YouTube IP blocks.

### Stage 2 — LLM generation (`generate_content`)

Makes two separate chat completion calls:

**Call 1 — Markdown summary.** The transcript (truncated to 14,000 characters if needed) is sent to the LLM using the summary system prompt, which instructs it to produce a structured markdown document covering an overview, major topics, key takeaways, and notable quotes.

**Call 2 — Podcast script.** The same transcript excerpt is sent again using a script system prompt chosen based on `--hosts`:
- In `--hosts 2` mode, the dialogue prompt instructs the LLM to write a back-and-forth conversation between two named hosts — Alex (analytical, detail-oriented) and Jordan (curious, big-picture) — formatted as `[ALEX]: ...` and `[JORDAN]: ...` lines.
- In `--hosts 1` mode, the monologue prompt instructs the LLM to write a flowing solo narration formatted as `[HOST]: ...` paragraphs.

The raw LLM output is then parsed line-by-line into a list of `(speaker, text)` tuples for the TTS stage.

The LLM client is built from the `openai` Python library with a configurable `base_url` and `api_key`, making it compatible with any OpenAI-compatible provider (OpenRouter, Ollama, Groq, etc.).

**Prompt loading (`load_prompt`).** Before each run, the three system prompts are resolved by `load_prompt()` using a three-level priority chain: a CLI-supplied file (e.g. `--dialogue-prompt`) takes precedence, followed by the corresponding file in the `prompts/` folder next to the script, followed by the hardcoded fallback string embedded in `podslacker.py`. This ensures the script always works out of the box while making every prompt trivially editable.

### Stage 3 — Markdown output (`build_markdown`)

The summary and the parsed script are assembled into a single markdown file. The file contains the source URL, the summary, a horizontal rule, then the full podcast script with each speaker's lines in bold. This file is also the cache target for `--reuse-summary`: if it already exists, the script stage is skipped entirely and the dialogue segments are re-parsed from the file by `parse_dialogue_from_markdown`.

### Stage 4 — Audio generation (`generate_audio`)

Each `(speaker, text)` segment is sent to OpenAI's `/audio/speech` endpoint one at a time. Two different voices are used — one per host — so listeners can tell speakers apart. The raw MP3 bytes returned by each TTS call are collected in a list. Between segments, a short burst of silent MP3 frames (~0.5 s) is appended to create a natural pause. All chunks are then concatenated and written to a single `.mp3` file. This approach requires no system-level audio tools like ffmpeg.

### Key design decisions

**Two separate clients.** The LLM client and the TTS client are constructed independently. This allows the LLM to be routed to an alternative provider (e.g. OpenRouter) while TTS always hits OpenAI's endpoint, since most alternative providers do not offer speech synthesis.

**Env-var key references.** API keys are never passed as command-line arguments. Instead, flags like `--llm-api-key-env` accept the *name* of an environment variable, keeping secrets out of shell history and process listings.

**No ffmpeg dependency.** MP3 is a frame-based format, so segments can be concatenated as raw bytes and the result plays back correctly in all standard media players. Silent frames are generated from a known-good MP3 frame header rather than by encoding silence, removing the need for any audio processing library.

**Externalised prompts with graceful fallback.** The system prompts that drive the LLM are stored as plain text files in a `prompts/` folder, making them editable without touching Python code. A three-level priority chain (CLI flag → default file → hardcoded string) means the script degrades gracefully: it works even if the `prompts/` folder is deleted, and a single flag is enough to swap in a completely different prompt for a one-off run.
