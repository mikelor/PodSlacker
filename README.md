# podslacker

Turn any YouTube video into a podcast. podslacker fetches a video's transcript, uses an LLM to write a markdown summary and a podcast script, then generates an MP3 using text-to-speech — all from the command line.

---

## Quick Start

```bash
# 1. Install dependencies
pip install youtube-transcript-api openai requests yt-dlp opencv-python-headless markdown

# 2. Set your OpenAI API key
export OPENAI_API_KEY=sk-...

# 3. (Optional) edit podslacker.json to set your preferred defaults

# 4. Run it
python podslacker.py https://www.youtube.com/watch?v=VIDEO_ID
```

Files created in the `outputs/` folder:
- `<video_id>_transcript.md` — the raw transcript retrieved from YouTube
- `<video_id>_summary.md` — structured markdown summary + podcast script
- `<video_id>_podcast.mp3` — the generated podcast audio (`.wav` when using Kokoro)
- `<video_id>_frame_01.jpg`, `_frame_02.jpg`, … — key-moment screenshots (5 by default; set `--num-frames 0` to skip)
- `<video_id>_page.html` — self-contained HTML page with rendered summary, image gallery, and built-in audio player (use `--no-page` to skip)

---

## Runtime Environment Setup

### Python version

Python 3.10 or later is required (the script uses `X | Y` union type hints).

### Dependencies

**Core (always required):**

```bash
pip install youtube-transcript-api openai requests
```

| Package | Version | Purpose |
|---|---|---|
| `youtube-transcript-api` | >= 1.0.0 | Fetches YouTube captions without the Data API |
| `openai` | >= 1.30.0 | LLM summarisation (chat completions) + TTS audio |
| `requests` | >= 2.28.0 | HTTP session used for cookie-based authentication |

**Frame capture (optional, enabled by default via `--num-frames 5`):**

```bash
pip install yt-dlp opencv-python-headless
```

| Package | Version | Purpose |
|---|---|---|
| `yt-dlp` | >= 2024.1.0 | Resolves a direct video stream URL without downloading the file |
| `opencv-python-headless` | >= 4.8.0 | Seeks to timestamps and captures JPEG frames from the stream |

If you do not want frame capture, pass `--num-frames 0` and you do not need these packages.

**HTML page generation (optional, enabled by default):**

```bash
pip install markdown
```

| Package | Version | Purpose |
|---|---|---|
| `markdown` | >= 3.5 | Converts the summary markdown to properly rendered HTML inside the page |

Without this package, the summary is displayed as escaped plain text — still readable, but without formatting. Pass `--no-page` to skip page generation entirely.

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
pip install youtube-transcript-api openai requests yt-dlp opencv-python-headless
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

### Config file

| Flag | Default | Description |
|---|---|---|
| `--config FILE` | `podslacker.json` (auto) | Path to a JSON configuration file. If not specified, podslacker automatically loads `podslacker.json` from the same directory as the script when it exists. CLI flags always override config file values. See [Configuration File](#configuration-file) for details. |

### General options

| Flag | Default | Description |
|---|---|---|
| `--output-dir DIR`, `-o DIR` | `outputs/` | Directory where all output files are saved. Created automatically if it doesn't exist. |
| `--hosts {1,2}` | `2` | Number of podcast hosts. `1` produces a solo monologue narrated by a single host. `2` produces a two-host dialogue between the two named hosts. |
| `--host1-name NAME` | `ALEX` | Name of the first host. Used as the speaker tag in the script, audio generation, and saved markdown. In `--hosts 1` mode this is the sole narrator. |
| `--host2-name NAME` | `JORDAN` | Name of the second host. Only applies in `--hosts 2` (dialogue) mode. |
| `--no-audio` | off | Skip TTS audio generation entirely. The transcript and summary markdown files are still written; only the audio file is omitted. |
| `--reuse-summary` | off | If a summary markdown for this video already exists in `--output-dir`, skip the LLM call and read the script directly from that file. Saves LLM API costs when re-generating audio. Falls back to LLM generation if the file is not found. |
| `--num-frames N` | `5` | Number of key-moment JPEG frames to capture from the video. The LLM automatically identifies the most significant timestamps. Set to `0` to disable entirely. Requires `yt-dlp` and `opencv-python-headless`. |
| `--no-page` | off | Skip HTML page generation. By default podslacker produces a self-contained `<video_id>_page.html` file that bundles everything into a single shareable file. |

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

### Per-step LLM overrides

Each of the three LLM-driven pipeline steps can use a completely independent model and API provider. Any value left unset inherits from the corresponding base `--llm-*` flag, so existing behaviour is unchanged unless you explicitly set one.

**Summary step**

| Flag | Default | Description |
|---|---|---|
| `--summary-model MODEL` | *(same as `--llm-model`)* | Model for the markdown summary call. |
| `--summary-base-url URL` | *(same as `--llm-base-url`)* | API base URL for the summary call. |
| `--summary-api-key-env VAR` | *(same as `--llm-api-key-env`)* | Env var holding the API key for the summary call. |

**Script step (dialogue / monologue)**

| Flag | Default | Description |
|---|---|---|
| `--script-model MODEL` | *(same as `--llm-model`)* | Model for the podcast script call. |
| `--script-base-url URL` | *(same as `--llm-base-url`)* | API base URL for the script call. |
| `--script-api-key-env VAR` | *(same as `--llm-api-key-env`)* | Env var holding the API key for the script call. |

**Key moments step**

| Flag | Default | Description |
|---|---|---|
| `--key-moments-model MODEL` | `openrouter/auto:free` | Model for key-moment timestamp identification. |
| `--key-moments-base-url URL` | *(same as `--llm-base-url`)* | API base URL for the key-moments call. |
| `--key-moments-api-key-env VAR` | *(same as `--llm-api-key-env`)* | Env var holding the API key for the key-moments call. |

### Prompt customisation options

These flags let you swap out the system prompts sent to the LLM without editing the script. Each accepts a path to a plain text file. See [Customising Prompts](#customising-prompts) for the full priority rules and editing guidance.

| Flag | Default | Description |
|---|---|---|
| `--summary-prompt FILE` | `prompts/summary.txt` | System prompt for the markdown summary LLM call. |
| `--dialogue-prompt FILE` | `prompts/dialogue.txt` | System prompt for the two-host dialogue LLM call (used when `--hosts 2`). |
| `--monologue-prompt FILE` | `prompts/monologue.txt` | System prompt for the solo monologue LLM call (used when `--hosts 1`). |
| `--key-moments-prompt FILE` | `prompts/key_moments.txt` | System prompt for the key-moment timestamp identification step. The literal text `{num_frames}` anywhere in the file is replaced at runtime with the value of `--num-frames`. |

### HTML page & GitHub Pages options

| Flag | Default | Description |
|---|---|---|
| `--no-page` | off | Skip HTML page generation. |
| `--publish-github` | off | After generating the page, publish it to GitHub Pages. Requires a GitHub Personal Access Token with `repo` and `pages` scopes stored in the env var named by `--github-token-env`. |
| `--github-repo REPO` | `podslacker-pages` | GitHub repository name to publish to. Created automatically as a public repo if it doesn't already exist. |
| `--github-token-env VAR` | `GITHUB_TOKEN` | Name of the environment variable that holds the GitHub Personal Access Token. |
| `--github-branch BRANCH` | `gh-pages` | Branch in the GitHub repository to publish to. Created from the default branch if it doesn't exist. |

### TTS engine

| Flag | Default | Description |
|---|---|---|
| `--tts-engine {openai,kokoro}` | `openai` | Which TTS engine to use. `openai` calls the OpenAI speech API (requires an API key, outputs MP3). `kokoro` runs a local open-source model on CPU (free, no API key, outputs WAV). |

### OpenAI TTS options

Used when `--tts-engine openai` (the default).

| Flag | Default | Description |
|---|---|---|
| `--tts-model MODEL` | `tts-1` | OpenAI TTS model. Use `tts-1-hd` for higher quality audio at a higher cost per character. |
| `--tts-api-key-env VAR` | `OPENAI_API_KEY` | Name of the env var holding the TTS API key. Only needed if your TTS key is different from your LLM key. |
| `--voice-host1 VOICE` | `onyx` | OpenAI voice for host 1 (`--host1-name`). Used as the sole narrator in `--hosts 1` mode. |
| `--voice-host2 VOICE` | `nova` | OpenAI voice for host 2 (`--host2-name`). Only applies in `--hosts 2` mode. |

**Available OpenAI voices:** `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer`

### Kokoro TTS options

Used when `--tts-engine kokoro`. Kokoro is a high-quality open-source TTS model that runs locally on CPU. No API key is required. On first run, model weights (~82 MB) are downloaded automatically from HuggingFace. Output is a `.wav` file.

Install before use:
```bash
pip install kokoro
```

> **HuggingFace authentication warning**
>
> When using Kokoro you may see:
> ```
> Warning: You are sending unauthenticated requests to the HF Hub.
> Please set a HF_TOKEN to enable higher rate limits and faster downloads.
> ```
> This is harmless — Kokoro still works — but you can silence it by authenticating with HuggingFace. Pick either option:
>
> **Option A — Environment variable (quickest):**
> Create a free account at [huggingface.co](https://huggingface.co), generate a read-only token at https://huggingface.co/settings/tokens, then set it in your shell:
> ```bash
> export HF_TOKEN=hf_...
> ```
>
> **Option B — CLI login (persists across sessions):**
> ```bash
> pip install huggingface_hub
> huggingface-cli login
> ```
> This stores your token in `~/.cache/huggingface/token` so you never have to set the env var again.

| Flag | Default | Description |
|---|---|---|
| `--kokoro-voice-host1 VOICE` | `am_michael` | Kokoro voice ID for host 1 (`--host1-name`). Used as the sole narrator in `--hosts 1` mode. |
| `--kokoro-voice-host2 VOICE` | `af_heart` | Kokoro voice ID for host 2 (`--host2-name`). Only applies in `--hosts 2` mode. |
| `--kokoro-lang {a,b}` | `a` | Language variant: `a` = American English, `b` = British English. |
| `--kokoro-speed RATE` | `1.0` | Speech rate multiplier. Try `1.1` for slightly faster delivery. |

**Available Kokoro voices:**

| Accent | Gender | Voice IDs |
|---|---|---|
| American (`--kokoro-lang a`) | Male | `am_michael`, `am_adam` |
| American (`--kokoro-lang a`) | Female | `af_heart`, `af_bella`, `af_nicole`, `af_sarah`, `af_sky` |
| British (`--kokoro-lang b`) | Male | `bm_george`, `bm_lewis` |
| British (`--kokoro-lang b`) | Female | `bf_emma`, `bf_isabella` |

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

**Use Kokoro for free local TTS (no API key for audio):**
```bash
pip install kokoro
python podslacker.py https://youtu.be/VIDEO_ID --tts-engine kokoro
# Outputs: <video_id>_podcast.wav
```

**Kokoro with British voices:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --tts-engine kokoro \
  --kokoro-lang b \
  --kokoro-voice-host1 bm_george \
  --kokoro-voice-host2 bf_emma
```

**Fully free run — local LLM (Ollama) + local TTS (Kokoro):**
```bash
pip install kokoro
python podslacker.py https://youtu.be/VIDEO_ID \
  --llm-base-url http://localhost:11434/v1 \
  --llm-model llama3.2 \
  --llm-api-key-env OPENAI_API_KEY \
  --tts-engine kokoro
```

**Capture 8 key-moment frames instead of the default 5:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --num-frames 8
```

**Use a cheaper model just for frame analysis (saves cost):**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --llm-model gpt-4o \
  --key-moments-model gpt-4o-mini
```

**Use a custom key moments prompt:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --key-moments-prompt ~/prompts/tech_talk_moments.txt
```

**Run without frame capture (no yt-dlp / OpenCV needed):**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --num-frames 0
```

**Custom host names:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --host1-name SAM \
  --host2-name TAYLOR
```

**Generate only the HTML page (no audio, no frames):**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --no-audio --num-frames 0
# Produces: <video_id>_page.html with the rendered summary
```

**Skip page generation:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID --no-page
```

**Publish the page to GitHub Pages:**
```bash
export GITHUB_TOKEN=ghp_...   # Personal Access Token with repo + pages scopes
python podslacker.py https://youtu.be/VIDEO_ID --publish-github
# Outputs: https://<username>.github.io/podslacker-pages/<video_id>_page.html
```

**Publish to a custom repo and branch:**
```bash
python podslacker.py https://youtu.be/VIDEO_ID \
  --publish-github \
  --github-repo my-podcast-site \
  --github-branch main
```

**Use a different model and provider for each step:**
```bash
export OPENAI_API_KEY=sk-...
export OPENROUTER_API_KEY=sk-or-...
python podslacker.py https://youtu.be/VIDEO_ID \
  --llm-base-url https://openrouter.ai/api/v1 \
  --llm-model anthropic/claude-3-5-sonnet \
  --llm-api-key-env OPENROUTER_API_KEY \
  --summary-model openai/gpt-4o \
  --summary-base-url https://api.openai.com/v1 \
  --summary-api-key-env OPENAI_API_KEY \
  --key-moments-model openrouter/auto:free \
  --key-moments-base-url https://openrouter.ai/api/v1 \
  --key-moments-api-key-env OPENROUTER_API_KEY
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

## Configuration File

`podslacker.json` (in the same folder as the script) is loaded automatically on every run. It lets you set persistent defaults for any flag without typing them on the command line each time. CLI flags always take precedence over config file values, which in turn take precedence over the hardcoded defaults.

### Priority order

```
CLI flag  >  podslacker.json  >  argparse hardcoded default
```

### Example config

```json
{
  "_comment": "My personal podslacker defaults",

  "output_dir": "~/podcasts",
  "hosts": 2,
  "host1_name": "ALEX",
  "host2_name": "JORDAN",
  "num_frames": 3,

  "llm_base_url": "https://openrouter.ai/api/v1",
  "llm_model": "anthropic/claude-3-5-sonnet",
  "llm_api_key_env": "OPENROUTER_API_KEY",

  "summary_model": null,
  "summary_base_url": null,
  "summary_api_key_env": null,

  "script_model": null,
  "script_base_url": null,
  "script_api_key_env": null,

  "key_moments_model": "openrouter/auto:free",
  "key_moments_base_url": null,
  "key_moments_api_key_env": null,

  "tts_engine": "openai",
  "tts_model": "tts-1-hd",
  "voice_host1": "onyx",
  "voice_host2": "nova",

  "kokoro_voice_host1": "am_michael",
  "kokoro_voice_host2": "af_heart",
  "kokoro_lang": "a",
  "kokoro_speed": 1.0,

  "no_page": false,
  "publish_github": false,
  "github_repo": "podslacker-pages",
  "github_token_env": "GITHUB_TOKEN",
  "github_branch": "gh-pages"
}
```

### Rules

Keys that match CLI flag names with hyphens replaced by underscores (e.g. `--llm-base-url` → `"llm_base_url"`). Set a key to `null` (or omit it) to fall back to the argparse default — null entries are never applied. Keys beginning with `_` are treated as comments and ignored. An unrecognised key prints a warning but does not abort the run.

### Using a different config file

```bash
python podslacker.py https://youtu.be/VIDEO_ID --config ~/my_setup.json
```

---

## Customising Prompts

The LLM calls that generate the markdown summary and the podcast script are driven by system prompts stored as plain text files. You can edit these freely without touching the Python code.

### Prompt files

The `prompts/` folder next to `podslacker.py` contains the four default prompt files:

| File | Used for |
|---|---|
| `prompts/summary.txt` | The markdown summary (both `--hosts` modes) |
| `prompts/dialogue.txt` | The two-host conversation (`--hosts 2`) |
| `prompts/monologue.txt` | The solo narration (`--hosts 1`) |
| `prompts/key_moments.txt` | Key-moment timestamp identification for frame capture |

Edit any of these files to change the LLM's behaviour for every future run.

### Priority order

Each prompt is resolved using the following priority, from highest to lowest:

1. **CLI flag** — `--summary-prompt FILE`, `--dialogue-prompt FILE`, `--monologue-prompt FILE`, or `--key-moments-prompt FILE`. If provided, this file is always used.
2. **Default prompt file** — the corresponding file in `prompts/` next to the script, if it exists and is non-empty.
3. **Built-in fallback** — the prompt string hardcoded inside `podslacker.py`, used automatically if both the CLI flag and the default file are absent or empty.

This means the script always works out of the box, even if the `prompts/` folder is missing.

### Host name placeholders

The dialogue and monologue prompts support `{host1_name}` and `{host2_name}` placeholders, which are substituted at runtime with the values of `--host1-name` and `--host2-name`. The default prompt files already use these, so changing the host names via config or CLI automatically flows through to the LLM instruction and the parsed output without any manual prompt editing.

If you write a custom dialogue or monologue prompt, include these placeholders wherever you reference the hosts. For example:

```
[{host1_name}]: <what {host1_name} says>
[{host2_name}]: <what {host2_name} says>
```

The script parser uses the configured names to detect speaker lines, so the placeholder in the output format line is what ties the name to the parsed segment.

### The `{num_frames}` placeholder

The key moments prompt (`prompts/key_moments.txt`) supports one special placeholder: `{num_frames}`. When podslacker runs, it substitutes this with the actual value of `--num-frames` before sending the prompt to the LLM. This lets the prompt instruct the model to return precisely the right number of timestamps. Keep `{num_frames}` somewhere in your custom prompt wherever you want the count to appear. For example:

```
Return ONLY a JSON array of exactly {num_frames} timestamps in seconds.
```

### Tips for writing prompts

The dialogue and monologue prompts must produce lines whose speaker tags exactly match the configured host names. With the defaults this looks like:

```
[ALEX]: <text>        ← dialogue, host 1
[JORDAN]: <text>      ← dialogue, host 2
[ALEX]: <text>        ← monologue (host1_name is the sole narrator)
```

If you set `--host1-name SAM --host2-name TAYLOR` the parser will look for `[SAM]:` and `[TAYLOR]:` instead. The `{host1_name}` and `{host2_name}` placeholders in the prompt files handle this automatically, so custom prompts that use the placeholders will always stay in sync with whatever names you configure.

The summary prompt has no format constraints and can be customised freely.

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

podslacker is a single-file Python script with six clearly separated stages that run in sequence.

```
YouTube URL
    │
    ▼
┌──────────────────────────┐
│  1. Transcript fetch      │  youtube-transcript-api
└──────────────────────────┘
    │  writes outputs/<video_id>_transcript.md
    │  raw text + timed entries (timestamp per caption)
    ▼
┌──────────────────────────┐
│  2. LLM generation        │  OpenAI-compatible chat completions
└──────────────────────────┘
    │  markdown summary + podcast script
    ▼
┌──────────────────────────┐
│  3. File output           │  writes outputs/<video_id>_summary.md
└──────────────────────────┘
    │  script segments
    ▼
┌──────────────────────────┐
│  4. TTS audio             │  OpenAI (MP3)  or  Kokoro local (WAV)
└──────────────────────────┘
    │
    ▼
outputs/<video_id>_podcast.mp3  (or .wav)

    │  (if --num-frames > 0)
    ▼
┌──────────────────────────┐
│  5. Frame capture         │  LLM → yt-dlp → OpenCV
└──────────────────────────┘
    │
    ▼
outputs/<video_id>_frame_01.jpg … _frame_N.jpg

    │  (unless --no-page)
    ▼
┌──────────────────────────┐
│  6. HTML page             │  markdown + base64 assets
└──────────────────────────┘
    │
    ▼
outputs/<video_id>_page.html
    │  (if --publish-github)
    ▼
https://<user>.github.io/<repo>/<video_id>_page.html
```

### Stage 1 — Transcript fetch (`fetch_transcript`)

Uses `youtube-transcript-api` to download the video's auto-generated or manually uploaded captions. Each caption snippet carries a start timestamp (in seconds) and a short text fragment. The function returns both the full concatenated text (used for LLM summarisation) and the raw list of `(start_seconds, text)` timed entries (used later for frame capture). If English captions are not available, the library falls back to any other available language. Optionally, a `requests.Session` loaded with browser cookies or a `GenericProxyConfig` is injected into the API client to work around YouTube IP blocks.

Once fetched, the transcript is immediately saved to `outputs/<video_id>_transcript.md` — a plain markdown file with a header containing the source URL and video ID, followed by the caption entries written one per line with a `[MM:SS]` timestamp prefix. This happens unconditionally before any LLM call, so the transcript is preserved even if later stages fail.

### Stage 2 — LLM generation (`generate_summary` + `generate_script`)

Makes two separate chat completion calls, each using its own independently configured client and model:

**Call 1 — Markdown summary (`generate_summary`).** The transcript (truncated to 14,000 characters if needed) is sent to the LLM using the summary system prompt, which instructs it to produce a structured markdown document covering an overview, major topics, key takeaways, and notable quotes. Uses the `--summary-*` client settings, falling back to the base `--llm-*` config.

**Call 2 — Podcast script (`generate_script`).** The same transcript excerpt is sent using a script system prompt chosen based on `--hosts`. The host names from `--host1-name` and `--host2-name` are substituted into the prompt via `{host1_name}` / `{host2_name}` placeholders before the call is made:
- In `--hosts 2` mode, the dialogue prompt produces a back-and-forth conversation formatted as `[host1_name]: ...` and `[host2_name]: ...` lines.
- In `--hosts 1` mode, the monologue prompt produces a flowing solo narration formatted as `[host1_name]: ...` paragraphs.

The raw LLM output is parsed line-by-line using the configured host names as speaker tags, producing a list of `(speaker, text)` tuples for the TTS stage. Uses the `--script-*` client settings, falling back to the base `--llm-*` config.

**Prompt loading (`load_prompt`).** Before each run, all four system prompts are resolved by `load_prompt()` using a three-level priority chain: a CLI-supplied file (e.g. `--dialogue-prompt`) takes precedence, followed by the corresponding file in the `prompts/` folder next to the script, followed by the hardcoded fallback string embedded in `podslacker.py`. This ensures the script always works out of the box while making every prompt trivially editable.

### Stage 3 — Markdown output (`build_markdown`)

The summary and the parsed script are assembled into `outputs/<video_id>_summary.md`. The file contains the source URL and video ID, the LLM-generated summary, a horizontal rule, then the full podcast script with each speaker's lines in bold. This file is also the cache target for `--reuse-summary`: if it already exists, the LLM stage is skipped entirely and the dialogue segments are re-parsed from it by `parse_dialogue_from_markdown`.

### Stage 4 — Audio generation

Two TTS engines are available, selected by `--tts-engine`.

**`generate_audio` (OpenAI, default):** Each `(speaker, text)` segment is sent to OpenAI's `/audio/speech` endpoint one at a time. Two different voices are used — one per host — so listeners can tell speakers apart. The raw MP3 bytes returned by each call are collected in a list; between segments, a short burst of silent MP3 frames (~0.6 s) is appended as a natural pause. All chunks are concatenated and written to a single `.mp3` file. No ffmpeg required.

**`generate_audio_kokoro` (Kokoro, local):** A `KPipeline` is created pointing at the `hexgrad/Kokoro-82M` model on HuggingFace (downloaded once on first run, ~82 MB). Each segment is synthesised as a `torch.FloatTensor` audio array at 24 kHz. Long segments are automatically split and re-joined by the pipeline. Between segments, a 0.6 s array of zeros is appended as silence. The combined numpy array is written to a `.wav` file using Python's stdlib `wave` module — no ffmpeg, no soundfile library required.

### Key design decisions

**Per-step LLM clients.** Each of the three LLM-driven steps (summary, script, key moments) gets its own OpenAI client built from its own base URL and API key env var. When a step's settings are identical to the base `--llm-*` config, the same client object is reused rather than opening a redundant connection. The TTS client is constructed independently from all of these, since TTS always hits OpenAI's endpoint regardless of which LLM provider is in use.

**Env-var key references.** API keys are never passed as command-line arguments. Instead, flags like `--llm-api-key-env` accept the *name* of an environment variable, keeping secrets out of shell history and process listings.

**No ffmpeg dependency.** MP3 is a frame-based format, so segments can be concatenated as raw bytes and the result plays back correctly in all standard media players. Silent frames are generated from a known-good MP3 frame header rather than by encoding silence, removing the need for any audio processing library.

**Pluggable TTS engines.** The audio generation step is cleanly separated from the rest of the pipeline. Adding a new engine only requires a new `generate_audio_*` function and a branch in `main()`. The existing OpenAI path is completely unchanged when `--tts-engine openai` is used.

**Externalised prompts with graceful fallback.** The system prompts that drive the LLM are stored as plain text files in a `prompts/` folder, making them editable without touching Python code. A three-level priority chain (CLI flag → default file → hardcoded string) means the script degrades gracefully: it works even if the `prompts/` folder is deleted, and a single flag is enough to swap in a completely different prompt for a one-off run.

**JSON configuration file.** `podslacker.json` is loaded automatically at startup and used to populate argparse defaults before the full parse runs. This is implemented as a two-pass parse: a quick `parse_known_args()` call extracts `--config` (if given), the file is loaded, and `parser.set_defaults()` injects its values — ensuring CLI flags still win. `null` values in the file are skipped so missing or unset fields always fall back to the hardcoded argparse defaults.

### Stage 5 — Frame capture (`identify_key_moments`, `get_video_stream_url`, `capture_frames`)

This optional stage (disabled with `--num-frames 0`) runs after audio generation and produces JPEG screenshots of key moments in the video without downloading the full file.

**`identify_key_moments`.** Sends the LLM a condensed version of the timed transcript — one entry sampled roughly every 10 seconds — along with an instruction to return exactly N timestamps as a JSON array. The timestamps are chosen to represent visually interesting or topically significant moments spread across the video. The LLM's response is parsed with `json.loads`; any markdown fencing is stripped first.

**`get_video_stream_url`.** Uses the `yt-dlp` Python API with `skip_download=True` and a format selector capped at 720p to resolve the direct HTTP URL of the video stream without touching the disk. This URL is valid for a session and is passed directly to OpenCV.

**`capture_frames`.** Opens the stream URL as a `cv2.VideoCapture` object, seeks to each timestamp using `cv2.CAP_PROP_POS_MSEC`, and writes the decoded frame as a JPEG at 92% quality. Frames are named sequentially — `<video_id>_frame_01.jpg`, `_frame_02.jpg`, etc. — and saved to the same `outputs/` directory as all other files. The capture object is always released in a `finally` block even if an individual frame fails.

**Why stream-only (no download).** Downloading a full video just to extract a few frames wastes bandwidth and storage. yt-dlp's stream URL approach lets OpenCV seek directly to any position in the remote file using HTTP range requests, so frames are captured in seconds with no temporary video file on disk.

### Stage 6 — HTML page generation (`generate_page`, `publish_to_github`)

This stage (skipped with `--no-page`) produces a single self-contained `.html` file that packages everything into one shareable document.

**`generate_page`.** Reads the summary markdown, any captured JPEG frames, and the audio file. The markdown is rendered to HTML using the `markdown` Python package (falling back to escaped plain text if the package isn't installed). Audio and images are base64-encoded and embedded directly into the HTML, so the page has no external dependencies and opens in any browser without a web server. The visual design is a dark-themed responsive layout with: a rendered prose section for the summary, a CSS grid gallery for the key-moment frames, and a custom audio player pinned to the bottom of the viewport. The player has a play/pause button, a seekable progress bar, and a live time display. The spacebar toggles playback.

**`publish_to_github`.** When `--publish-github` is set, this function uploads the generated page to a GitHub repository using the GitHub REST API. It authenticates with a Personal Access Token, creates the target repository if it doesn't exist (set to public, auto-initialised), creates the `gh-pages` branch if needed (from the default branch), then creates or updates the file using a PUT to the Contents API. Finally, it enables GitHub Pages on the repository if it hasn't been configured yet. The returned URL follows the pattern `https://<username>.github.io/<repo>/<video_id>_page.html`.
