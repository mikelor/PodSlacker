# PodSlacker

> Turn any YouTube video into a podcast episode — automatically.

PodSlacker fetches a video's transcript, uses an LLM to write a structured summary and a podcast script, synthesises audio via text-to-speech, captures key-moment screenshots, and packages everything into a self-contained HTML page. Optionally publishes to GitHub Pages.

Built on **.NET 10 / C# 14** with no Python, no ffmpeg, and no yt-dlp required. Kokoro local TTS is the default — **no API key needed for audio**.

---

## Features

- **No extra runtimes.** Pure .NET 10 — install the SDK and you're done.
- **Local TTS by default.** [Kokoro](https://github.com/thewh1teagle/kokoro-onnx) runs on CPU, produces high-quality 24 kHz WAV, and requires no API key. Switch to OpenAI TTS with one flag.
- **Any OpenAI-compatible LLM.** Works with OpenRouter, Ollama, Groq, Azure OpenAI, Together AI — or vanilla OpenAI. Each pipeline step can use a different provider.
- **No yt-dlp dependency.** Transcripts, metadata, and video stream URLs are all resolved natively via [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode).
- **No ffmpeg dependency.** MP3 segments are concatenated as raw bytes; WAV files are written with a hand-written RIFF header.
- **Self-contained HTML output.** Audio and screenshots are base64-embedded — the page opens in any browser, offline, with no web server.
- **GitHub Pages publishing.** One flag uploads the page and enables Pages automatically.
- **Prompt-driven.** All LLM instructions live in plain-text files you can edit freely, with a three-tier fallback so the tool always works out of the box.

---

## Quick Start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and an LLM API key (see [LLM Providers](#llm-providers) — OpenRouter has a free tier).

```bash
# 1. Clone and build
git clone https://github.com/your-username/PodSlacker.git
cd PodSlacker/src
dotnet build PodSlacker.sln -c Release

# 2. Set your LLM API key (OpenRouter used in podslacker.json by default)
export OPENROUTER_API_KEY=sk-or-...

# 3. Run it — Kokoro TTS is the default, so no audio API key is needed
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

Files written to `outputs/`:

| File | Contents |
|---|---|
| `*_transcript.txt` | Timestamped captions `[MM:SS] text` |
| `*_summary.md` | LLM summary + full podcast script |
| `*_podcast.wav` | Stitched audio (Kokoro) or `*_podcast.mp3` (OpenAI TTS) |
| `*_frame_01.jpg` … | Key-moment screenshots (6 by default) |
| `*_page.html` | Self-contained page with summary, gallery, and audio player |

### Want a fully free, fully local run?

```bash
# Start Ollama with any model
ollama run llama3.2

# Run with local LLM + local Kokoro TTS — zero API keys
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --llm-base-url http://localhost:11434/v1 \
  --llm-model llama3.2 \
  --llm-api-key-env OPENAI_API_KEY
```

### Or use OpenAI for everything

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --llm-api-key-env OPENAI_API_KEY \
  --llm-model gpt-4o \
  --tts-engine openai
```

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0 or later | The only system-level dependency |
| LLM API key | — | OpenRouter, OpenAI, or any compatible provider |
| OpenAI API key *(optional)* | — | Only needed when `--tts-engine openai` |
| [Kokoro ONNX model](https://github.com/taylorchu/kokoro-onnx) (~320 MB) | auto-downloaded | Downloaded once on first Kokoro TTS run |

> **Frame capture on Linux/macOS:** The project currently references `OpenCvSharp4.runtime.win`. To capture frames on Linux, add the appropriate `OpenCvSharp4.runtime.ubuntu.*` package to `PodSlacker.Core.csproj`, or pass `--num-frames 0` to skip frame capture entirely.

---

## Building

```bash
cd src
dotnet build PodSlacker.sln -c Release
```

To publish a self-contained executable:

```bash
# Windows x64
dotnet publish PodSlacker.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish PodSlacker.Cli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# macOS Apple Silicon
dotnet publish PodSlacker.Cli -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

---

## Configuration File

`podslacker.json` (next to the executable, or in the working directory) is loaded automatically. CLI flags always take precedence. Keys starting with `_` are treated as comments; `null` values fall through to the compiled default.

```json
{
  "_comment": "PodSlacker defaults — CLI flags override everything here",

  "output_dir": "outputs",
  "hosts": 2,
  "host1_name": "MIKE",
  "host2_name": "JORDAN",
  "num_frames": 6,

  "_llm": "Use any OpenAI-compatible endpoint",
  "llm_base_url": "https://openrouter.ai/api/v1",
  "llm_model": "openrouter/auto:free",
  "llm_api_key_env": "OPENROUTER_API_KEY",

  "_tts": "kokoro = local, free. openai = cloud, requires API key.",
  "tts_engine": "kokoro",
  "voice_host1": "am_michael",
  "voice_host2": "af_heart",
  "tts_model": "tts-1",
  "tts_api_key_env": "OPENAI_API_KEY",

  "_prompts": "Create these files to override the embedded defaults.",
  "summary_prompt": "prompts/summary.txt",
  "dialogue_prompt": "prompts/dialogue.txt",
  "monologue_prompt": "prompts/monologue.txt",
  "key_moments_prompt": "prompts/key_moments.txt",

  "_github": "Set publish_github to true and provide a GITHUB_TOKEN",
  "publish_github": false,
  "github_repo": "podslacker-pages",
  "github_token_env": "GITHUB_TOKEN",
  "github_branch": "gh-pages"
}
```

Use `--config /path/to/custom.json` to load a different file.

---

## CLI Reference

```
podslacker generate <url> [options]
podslacker status <job-id> --server <url>
```

### General

| Flag | Default | Description |
|---|---|---|
| `--output-dir, -o DIR` | `outputs` | Directory for all generated files |
| `--hosts {1,2}` | `2` | `1` = solo monologue, `2` = two-host dialogue |
| `--host1-name NAME` | `MIKE` | Name for the first (or only) host |
| `--host2-name NAME` | `JORDAN` | Name for the second host |
| `--no-audio` | off | Skip TTS synthesis |
| `--no-page` | off | Skip HTML page generation |
| `--reuse-summary` | off | Skip LLM calls if `*_summary.md` already exists |
| `--num-frames N` | `6` | Key-moment screenshots to capture; `0` to disable |
| `--config FILE` | auto | Path to a JSON config file |

### YouTube Access

| Flag | Default | Description |
|---|---|---|
| `--cookies FILE` | none | Netscape `cookies.txt` for age-restricted videos |
| `--proxy URL` | none | Proxy URL, e.g. `http://user:pass@host:port` |

### LLM Provider

| Flag | Default | Description |
|---|---|---|
| `--llm-model MODEL` | `openrouter/auto:free` | Model identifier |
| `--llm-base-url URL` | `https://openrouter.ai/api/v1` | Base URL for the chat completions endpoint |
| `--llm-api-key-env VAR` | `OPENROUTER_API_KEY` | Env var holding the API key |

To use plain OpenAI instead, pass `--llm-base-url` *(omit for OpenAI)*, `--llm-model gpt-4o`, and `--llm-api-key-env OPENAI_API_KEY`.

Each pipeline step (summary, script, key-moments) can override these independently using `--summary-model`, `--summary-base-url`, `--summary-api-key-env`, and equivalents for `--script-*` and `--key-moments-*`.

### TTS Engine

| Flag | Default | Description |
|---|---|---|
| `--tts-engine {kokoro,openai}` | `kokoro` | TTS backend |

**Voice selection** (`--voice-host1`, `--voice-host2`)

The same two flags work for both engines — just use the right voice name for whichever engine is active.

| Flag | Default | Description |
|---|---|---|
| `--voice-host1 VOICE` | `am_michael` | Voice for host 1 (Kokoro or OpenAI name) |
| `--voice-host2 VOICE` | `af_heart` | Voice for host 2 (Kokoro or OpenAI name) |

Voices used in the default config:

| Engine | Host | Voice | Style |
|---|---|---|---|
| Kokoro | Host 1 | `am_michael` | American English male |
| Kokoro | Host 2 | `af_heart` | American English female |
| OpenAI | Host 1 | `onyx` | Deep, authoritative male |
| OpenAI | Host 2 | `nova` | Warm, conversational female |

Full voice lists: [Kokoro voices (KokoroSharp)](https://github.com/thewh1teagle/kokoro-onnx) · [OpenAI TTS voices](https://platform.openai.com/docs/guides/text-to-speech)

**OpenAI TTS model** (ignored when using Kokoro):

| Flag | Default | Description |
|---|---|---|
| `--tts-model MODEL` | `tts-1` | `tts-1` (faster) or `tts-1-hd` (higher quality) |
| `--tts-api-key-env VAR` | `OPENAI_API_KEY` | Env var for the TTS API key |

### Prompt Overrides

| Flag | Default | Description |
|---|---|---|
| `--summary-prompt FILE` | embedded | Custom system prompt for the summary step |
| `--dialogue-prompt FILE` | embedded | Custom prompt for two-host dialogue |
| `--monologue-prompt FILE` | embedded | Custom prompt for solo narration |
| `--key-moments-prompt FILE` | embedded | Custom prompt for key-moment selection |

Prompts are resolved in this order: CLI flag → `prompts/` folder next to the executable → embedded resource in `PodSlacker.Core.dll`.

### GitHub Pages

| Flag | Default | Description |
|---|---|---|
| `--publish-github` | off | Publish the HTML page to GitHub Pages |
| `--github-repo REPO` | `podslacker-pages` | Repository name (created if missing) |
| `--github-token-env VAR` | `GITHUB_TOKEN` | Env var for a PAT with `repo` + `pages` scopes |
| `--github-branch BRANCH` | `gh-pages` | Branch used as the Pages source |

---

## LLM Providers

Any OpenAI-compatible endpoint works. Set `--llm-base-url` and `--llm-api-key-env` (or the equivalents in `podslacker.json`):

| Provider | `llm_base_url` | Example model |
|---|---|---|
| OpenAI | *(omit)* | `gpt-4o`, `gpt-4o-mini` |
| OpenRouter | `https://openrouter.ai/api/v1` | `google/gemini-2.0-flash-001`, `anthropic/claude-3-5-sonnet` |
| Ollama (local) | `http://localhost:11434/v1` | `llama3.2`, `mistral`, `phi4` |
| Groq | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` |
| Azure OpenAI | `https://<resource>.openai.azure.com/` | `gpt-4o` |
| Together AI | `https://api.together.xyz/v1` | `meta-llama/Llama-3-70b-chat-hf` |

---

## Usage Examples

**Regenerate audio without re-running the LLM:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --reuse-summary
```

**British voices:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --voice-host1 bm_george \
  --voice-host2 bf_emma
```

**Solo monologue, no frames:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --hosts 1 --num-frames 0
```

**Publish to GitHub Pages:**
```bash
export GITHUB_TOKEN=ghp_...
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --publish-github
```

**Use different models per step (summary on Claude, everything else on Gemini):**
```bash
export OPENROUTER_API_KEY=sk-or-...
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --llm-base-url https://openrouter.ai/api/v1 \
  --llm-model google/gemini-2.0-flash-001 \
  --llm-api-key-env OPENROUTER_API_KEY \
  --summary-model anthropic/claude-3-5-sonnet \
  --summary-api-key-env OPENROUTER_API_KEY
```

**Custom host names and output folder:**
```bash
dotnet run --project PodSlacker.Cli -c Release -- generate \
  "https://www.youtube.com/watch?v=dQw4w9WgXcQ" \
  --host1-name SAM --host2-name TAYLOR \
  --output-dir ~/podcasts
```

---

## Architecture

The pipeline runs seven stages in sequence, reporting progress via an `IProgress<PipelineProgress>` sink (used by the CLI progress line and, in future, an SSE stream from a web API).

```
YouTube URL
    │
    ▼
┌──────────────────────────────────────┐
│  1. Fetch title                       │  YoutubeExplode → oEmbed fallback
└──────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────┐
│  2. Fetch transcript                  │  YoutubeExplode ClosedCaptions
└──────────────────────────────────────┘  (HTTP fallback tiers: watch-page JSON,
    │  writes *_transcript.txt             YouTubei API, timedtext XML)
    ▼
┌──────────────────────────────────────┐
│  3. LLM — summary + podcast script   │  IChatClient (any OpenAI-compatible)
└──────────────────────────────────────┘
    │  writes *_summary.md
    ▼
┌──────────────────────────────────────┐
│  4. TTS audio                         │  Kokoro (local WAV) or OpenAI (MP3)
└──────────────────────────────────────┘
    │  writes *_podcast.wav / .mp3
    ▼
┌──────────────────────────────────────┐
│  5. Frame capture                     │  LLM identifies timestamps
└──────────────────────────────────────┘  YoutubeExplode stream URL → OpenCvSharp
    │  writes *_frame_01.jpg …
    ▼
┌──────────────────────────────────────┐
│  6. HTML page                         │  Markdig renders markdown; audio +
└──────────────────────────────────────┘  images are base64-embedded
    │  writes *_page.html
    ▼
┌──────────────────────────────────────┐
│  7. GitHub Pages (optional)           │  Octokit creates/updates repo + branch,
└──────────────────────────────────────┘  enables Pages
    │
    ▼
https://<user>.github.io/<repo>/*_page.html
```

### Key design decisions

**`Microsoft.Extensions.AI` for LLM abstraction.** The `IChatClient` interface means OpenAI, OpenRouter, Ollama, and Azure OpenAI all work without any changes to service code. The caller wires up the concrete client; the pipeline never sees a provider name.

**Per-step LLM clients.** Summary, script generation, and key-moment identification each get their own `IChatClient`, built from their own base URL and API key env var. When a step's settings match the base config, the same client instance is reused.

**Env-var key references.** API keys are never passed as CLI arguments — only the *name* of the environment variable is passed. Secrets stay out of shell history and process listings.

**No ffmpeg.** MP3 is a frame-based format; segments concatenate cleanly as raw bytes. WAV files for Kokoro are written with a hand-crafted 44-byte RIFF/WAVE PCM header — no audio library required.

**Kokoro model management.** The 320 MB `kokoro.onnx` model is downloaded once to `AppContext.BaseDirectory` via a streamed HTTP download with 10%-bucket progress logging, using an atomic `.download → rename` pattern so a cancelled download never leaves a corrupt file.

**Embedded prompts.** All four system prompts are embedded resources in `PodSlacker.Core.dll`, so the CLI, a future web API, and Azure Functions all share the same canonical prompts with no file-copying during deployment. Users can override at three levels: CLI flag → `prompts/` folder next to the executable → embedded resource.

---

## Python Version

The original Python implementation lives in [`python/`](python/). It has a `--tts-engine kokoro` flag and supports the same basic pipeline but requires Python 3.10+, `pip` packages, and optionally `yt-dlp`. The .NET version supersedes it but the Python code is preserved for reference.

---

## Acknowledgements

PodSlacker is built on top of excellent open-source work. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the full list of packages, their authors, and their licenses.

---

## License

MIT — see [LICENSE](LICENSE).
